from __future__ import annotations

import asyncio
import hashlib
import ipaddress
import socket
from collections.abc import Awaitable, Callable, Iterable
from urllib.parse import parse_qsl, urlencode, urlsplit, urlunsplit


class UnsafeUrlError(ValueError):
    pass


_TRACKING_KEYS = {"fbclid", "gclid", "dclid", "msclkid", "mc_cid", "mc_eid"}


def normalize_url(value: str) -> str:
    parts = urlsplit(value.strip())
    if parts.scheme.lower() not in {"http", "https"} or not parts.hostname:
        raise UnsafeUrlError("URL must use HTTP(S) and include a host")
    if parts.username is not None or parts.password is not None:
        raise UnsafeUrlError("URLs containing credentials are not allowed")

    try:
        host = parts.hostname.rstrip(".").encode("idna").decode("ascii").lower()
    except UnicodeError as exc:
        raise UnsafeUrlError("URL contains an invalid hostname") from exc

    port = parts.port
    default_port = (parts.scheme.lower() == "http" and port == 80) or (
        parts.scheme.lower() == "https" and port == 443
    )
    netloc = host if port is None or default_port else f"{host}:{port}"
    path = parts.path or "/"
    query = [
        (key, item)
        for key, item in parse_qsl(parts.query, keep_blank_values=True)
        if not key.lower().startswith("utm_") and key.lower() not in _TRACKING_KEYS
    ]
    return urlunsplit((parts.scheme.lower(), netloc, path, urlencode(query, doseq=True), ""))


def url_hash(value: str) -> str:
    return "sha256:" + hashlib.sha256(normalize_url(value).encode()).hexdigest()


def content_hash(value: str | None) -> str | None:
    if not value:
        return None
    normalized = " ".join(value.split())
    return "sha256:" + hashlib.sha256(normalized.encode()).hexdigest()


def validate_resolved_addresses(addresses: Iterable[str]) -> None:
    resolved = list(addresses)
    if not resolved:
        raise UnsafeUrlError("Hostname did not resolve")
    for value in resolved:
        address = ipaddress.ip_address(value)
        if not address.is_global or any(
            (
                address.is_private,
                address.is_loopback,
                address.is_link_local,
                address.is_multicast,
                address.is_reserved,
                address.is_unspecified,
            )
        ):
            raise UnsafeUrlError(f"Destination resolves to a non-public address: {address}")


async def resolve_host(host: str, port: int) -> list[str]:
    results = await asyncio.to_thread(socket.getaddrinfo, host, port, type=socket.SOCK_STREAM)
    return sorted({result[4][0] for result in results})


async def validate_public_url(
    value: str,
    resolver: Callable[[str, int], Awaitable[list[str]]] = resolve_host,
) -> str:
    normalized = normalize_url(value)
    parts = urlsplit(normalized)
    assert parts.hostname is not None
    port = parts.port or (443 if parts.scheme == "https" else 80)
    validate_resolved_addresses(await resolver(parts.hostname, port))
    return normalized


def same_site(first: str, second: str) -> bool:
    def host(value: str) -> str:
        parsed = urlsplit(normalize_url(value))
        return (parsed.hostname or "").removeprefix("www.")

    return host(first) == host(second)
