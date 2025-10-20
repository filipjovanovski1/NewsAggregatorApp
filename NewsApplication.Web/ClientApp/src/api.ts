// Centralized API calls
import type { ArticleDto, PagedResult } from './types';

const API_BASE = '/api';

export interface ArticleQuery {
    take?: number;
    category?: string | null;
    page?: number;
    pageSize?: number;
}

/** Shared fetch wrapper that logs URL and surfaces server error text */
async function fetchJson<T>(url: URL, label: string): Promise<T> {
    const href = url.toString();
    console.debug(`[GET] ${label}`, href);
    const res = await fetch(href);

    if (!res.ok) {
        let body = '';
        try { body = await res.text(); } catch { /* ignore */ }
        // Include status and any ProblemDetails/exception text coming from ASP.NET
        throw new Error(`${label} failed: ${res.status} · ${body}`);
    }
    return res.json() as Promise<T>;
}

// ===== WORLD / TOP =====
export async function getTopWorldArticles(q?: ArticleQuery): Promise<PagedResult<ArticleDto>> {
    const url = new URL(`${API_BASE}/world/articles`, window.location.origin);
    if (q?.take != null) url.searchParams.set('take', String(q.take));
    if (q?.category) url.searchParams.set('category', String(q.category));
    if (q?.page != null) url.searchParams.set('page', String(q.page));
    if (q?.pageSize != null) url.searchParams.set('pageSize', String(q.pageSize));
    return fetchJson<PagedResult<ArticleDto>>(url, 'TopWorld fetch');
}

// ===== BY COUNTRY =====
export async function getCountryArticles(iso2: string, q?: ArticleQuery): Promise<PagedResult<ArticleDto>> {
    const url = new URL(`${API_BASE}/countries/${encodeURIComponent(iso2)}/articles`, window.location.origin);
    if (q?.take != null) url.searchParams.set('take', String(q.take));
    if (q?.category) url.searchParams.set('category', String(q.category));
    if (q?.page != null) url.searchParams.set('page', String(q.page));
    if (q?.pageSize != null) url.searchParams.set('pageSize', String(q.pageSize));
    return fetchJson<PagedResult<ArticleDto>>(url, 'Country fetch');
}

// ===== BY CITY =====
export async function getCityArticles(cityId: string | number, q?: ArticleQuery): Promise<PagedResult<ArticleDto>> {
    const url = new URL(`${API_BASE}/cities/${encodeURIComponent(String(cityId))}/articles`, window.location.origin);
    if (q?.take != null) url.searchParams.set('take', String(q.take));
    if (q?.category) url.searchParams.set('category', String(q.category));
    if (q?.page != null) url.searchParams.set('page', String(q.page));
    if (q?.pageSize != null) url.searchParams.set('pageSize', String(q.pageSize));
    return fetchJson<PagedResult<ArticleDto>>(url, 'City fetch');
}

// ===== SEARCH =====
export interface SearchResult {
    kind: 'country' | 'city';
    idOrIso: string | number;
    name: string;
    lat?: number;
    lng?: number;
    // if your backend returns iso2 explicitly for countries, great:
    iso2?: string;
}

export async function searchPlaces(query: string): Promise<SearchResult[]> {
    const url = new URL(`${API_BASE}/search`, window.location.origin);
    url.searchParams.set('q', query);
    return fetchJson<SearchResult[]>(url, 'Search');
}
