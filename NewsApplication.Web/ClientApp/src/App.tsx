import { useCallback, useMemo, useState, useEffect, useRef } from 'react';
import GlobeView from './components/GlobeView';
import SearchBar from './components/SearchBar';
import ArticleOverlay from './components/ArticleOverlay';
import {
    getTopWorldArticles,
    getCountryArticles,
    getCityArticles,
    searchPlaces
} from './api';
import type { LocationSelection, ArticleDto, PagedResult, ApiPlace } from './types';
import './styles.css';

function coerceIso2(v: unknown): string | null {
    if (typeof v !== 'string') return null;
    const up = v.trim().toUpperCase();
    return /^[A-Z]{2}$/.test(up) ? up : null;
}
// Accept either camelCase or PascalCase from the backend (paged shape)
type AnyPaged<T> = {
    items?: T[];
    Items?: T[];
    total?: number;
    Total?: number;
    page?: number;
    Page?: number;
    pageSize?: number;
    PageSize?: number;
};

// The backend may also return a plain array (IArticleReadService case)
type AnyPagedOrArray<T> = AnyPaged<T> | T[];

// Normalizer that handles both shapes
function normalizePaged<T>(r: AnyPagedOrArray<T>, page: number, pageSize: number): PagedResult<T> {
    if (Array.isArray(r)) {
        const total = r.length;
        const start = (page - 1) * pageSize;
        const end = start + pageSize;
        const items = r.slice(start, end);
        return { items, total, page, pageSize };
    }
    return {
        items: (r.items ?? r.Items ?? []) as T[],
        total: (r.total ?? r.Total ?? 0) as number,
        page: (r.page ?? r.Page ?? page) as number,
        pageSize: (r.pageSize ?? r.PageSize ?? pageSize) as number
    };
}

// ---- Fixed zoom levels ----
const CITY_ALT = 0.75;     // closer
const COUNTRY_ALT = 1.25;  // farther

type PlaceKind = 'city' | 'country';
//type ReverseMatch = { kind: PlaceKind; idOrIso: string; name: string; lat?: number; lng?: number; altitude?: number; countryIso2?: string; };
//type SearchHit = { kind: PlaceKind; idOrIso: string; name: string; lat?: number; lng?: number; altitude?: number; countryIso2?: string; };

function focusFor(kind: PlaceKind, lat: number, lng: number, altitudeHint?: number) {
    const altitude = typeof altitudeHint === 'number'
        ? altitudeHint
        : (kind === 'city' ? CITY_ALT : COUNTRY_ALT);
    return { lat, lng, altitude };
}

export default function App() {
    const [focus, setFocus] = useState<{ lat: number; lng: number; altitude?: number } | null>(null);
    const [selected, setSelected] = useState<LocationSelection | null>(null);
    const [page, setPage] = useState(1);
    const [data, setData] = useState<PagedResult<ArticleDto> | null>(null);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [overlayOpen, setOverlayOpen] = useState(false);
    const reqSeq = useRef(0);

    // ✅ 2 rows × 3 columns
    const pageSize = 6;
    const TAKE_WINDOW = 23; // must be >= pageSize for arrows to show

    const title = useMemo(() => {
        if (!selected) return null;
        return selected.kind === 'country'
            ? `Top news in ${selected.name ?? selected.iso2}`
            : `Top news in ${selected.name ?? selected.id}`;
    }, [selected]);

    // Top World fetch (pageable) so arrows also work on World view
    const fetchTopWorld = useCallback(async (pageNum = 1) => {
        const myId = ++reqSeq.current;
        setError(null);
        setLoading(true);
        try {
            const raw = (await getTopWorldArticles({ page: pageNum, pageSize, take: TAKE_WINDOW })) as unknown as AnyPagedOrArray<ArticleDto>;
            if (reqSeq.current !== myId) return;
            setSelected(null);
            setData(normalizePaged<ArticleDto>(raw, pageNum, pageSize));
            setOverlayOpen(false);
        } catch (e: unknown) {
            if (reqSeq.current !== myId) return;
            setError((e as { message?: string })?.message ?? 'Failed to fetch articles.');
            setData(null);
        } finally {
            if (reqSeq.current === myId) setLoading(false);
        }
    }, [pageSize, TAKE_WINDOW]);

    // Country/City fetch (pageable)
    const fetchForSelection = useCallback(async (sel: LocationSelection, pageNum = 1) => {
        const myId = ++reqSeq.current;
        setError(null);
        setLoading(true);
        try {
            let raw: AnyPagedOrArray<ArticleDto>;
            if (sel.kind === 'country') {
                const iso = coerceIso2(sel.iso2);
                if (!iso) {
                    throw new Error('Invalid country ISO2 code.');
                }
                raw = (await getCountryArticles(
                    iso,
                    { page: pageNum, pageSize, take: TAKE_WINDOW }
                )) as unknown as AnyPagedOrArray<ArticleDto>;
            } else {
                raw = (await getCityArticles(
                    sel.id,
                    { page: pageNum, pageSize, take: TAKE_WINDOW }
                )) as unknown as AnyPagedOrArray<ArticleDto>;
            }
            if (reqSeq.current !== myId) return;
            const result = normalizePaged<ArticleDto>(raw, pageNum, pageSize);
            setData(result);
        } catch (e: unknown) {
            if (reqSeq.current !== myId) return;
            setError((e as { message?: string })?.message ?? 'Failed to fetch articles.');
            setData(null);
        } finally {
            if (reqSeq.current === myId) setLoading(false);
        }
    }, [pageSize, TAKE_WINDOW]);

    const handleGlobePick = useCallback(async (lat: number, lng: number) => {
        setFocus({ lat, lng, altitude: COUNTRY_ALT });
        try {
            setLoading(true);
            setError(null);
            setOverlayOpen(true);
            const resp = await fetch(`/api/reverse?lat=${lat}&lng=${lng}`);
            if (resp.ok) {
                const match: ApiPlace = await resp.json();

                const fxLat = match.lat ?? lat;
                const fxLng = match.lng ?? lng;
                setFocus(focusFor(match.kind, fxLat, fxLng, match.altitude));

                if (match.kind === 'country') {
                    const iso2 =
                        coerceIso2((match as any).iso2) ??  // if your API returns an explicit iso2 use it
                        coerceIso2(match.idOrIso);          // otherwise trust idOrIso only if it’s exactly 2 letters

                    if (!iso2) {
                        setError('Country code unavailable for this selection.');
                        setLoading(false);
                        return;
                    }

                    const sel: LocationSelection = {
                        kind: 'country',
                        iso2,
                        name: match.name,
                        lat: fxLat,
                        lng: fxLng
                    };
                    setSelected(sel);
                    setPage(1);
                    await fetchForSelection(sel, 1);
                }

                 else {
                    const iso = match.countryIso2 ? String(match.countryIso2).toUpperCase() : undefined;
                    const sel: LocationSelection = {
                        kind: 'city',
                        id: String(match.idOrIso),                                   // normalize
                        name: match.name,
                        lat: fxLat,
                        lng: fxLng,
                        countryIso2: iso
                    };
                    setSelected(sel);
                    setPage(1);
                    await fetchForSelection(sel, 1);
                }
            }
        } finally {
            setLoading(false);
        }
    }, [fetchForSelection]);


    const handleSearch = useCallback(async (query: string) => {
        setLoading(true);
        setError(null);
        setOverlayOpen(true);
        try {
            const results: ApiPlace[] = await searchPlaces(query);   // idOrIso can be string | number
            if (results.length === 0) { setOverlayOpen(false); return; }

            const r = results[0];

            if (typeof r.lat === 'number' && typeof r.lng === 'number') {
                setFocus(focusFor(r.kind, r.lat, r.lng, r.altitude));
            }
            if (r.kind === 'country') {
                const iso2 =
                    coerceIso2((r as any).iso2) ??
                    coerceIso2(r.idOrIso);

                if (!iso2) {
                    setError('Could not resolve a valid 2-letter ISO code for this country.');
                    setLoading(false);
                    return;
                }

                const sel: LocationSelection = {
                    kind: 'country',
                    iso2,
                    name: r.name,
                    lat: r.lat,
                    lng: r.lng
                };
                setSelected(sel);
                setPage(1);
                await fetchForSelection(sel, 1);
            }  
            else {
                const iso = r.countryIso2 ? String(r.countryIso2).toUpperCase() : undefined;
                const sel: LocationSelection = {
                    kind: 'city',
                    id: String(r.idOrIso),                                // normalize
                    name: r.name,
                    lat: r.lat,
                    lng: r.lng,
                    countryIso2: iso
                };
                setSelected(sel); setPage(1); await fetchForSelection(sel, 1);
            }
        } finally {
            setLoading(false);
        }
    }, [fetchForSelection]);
    // at the top of App() with your other state/refs
    const whatsNewRef = useRef<HTMLButtonElement | null>(null);
    const searchBtnRef = useRef<HTMLButtonElement | null>(null);

    useEffect(() => {
        const update = () => {
            const w1 = whatsNewRef.current?.offsetWidth ?? 0; // What's New
            const w2 = searchBtnRef.current?.offsetWidth ?? 0; // Search
            const gaps = 12 + 8; // gap between SearchBar and What's New (12) + form gap (8)
            const total = w1 + w2 + gaps;
            document.documentElement.style.setProperty("--right-controls-width", `${total}px`);
        };

        // run now and on resize/element size changes
        update();
        const ro1 = new ResizeObserver(update);
        const ro2 = new ResizeObserver(update);
        if (whatsNewRef.current) ro1.observe(whatsNewRef.current);
        if (searchBtnRef.current) ro2.observe(searchBtnRef.current);
        window.addEventListener("resize", update);

        return () => {
            ro1.disconnect();
            ro2.disconnect();
            window.removeEventListener("resize", update);
        };
    }, []);


   

    // Carousel navigation (ordered by recency from server)
    const total = data?.total ?? 0;
    const canPrev = page > 1;
    const canNext = page * pageSize < total;

    const goPrev = async () => {
        if (!canPrev) return;
        const p = page - 1;
        setPage(p);
        if (selected) await fetchForSelection(selected, p);
        else await fetchTopWorld(p);
    };

    const goNext = async () => {
        if (!canNext) return;
        const p = page + 1;
        setPage(p);
        if (selected) await fetchForSelection(selected, p);
        else await fetchTopWorld(p);
    };

    return (
        <div className="app">
            {/* Top bar: SearchBar + right-aligned “What’s New” in ONE row */}
            <div className="topbar">
                <div className="search-with-button">
                    <div className="search-wrap">
                        {/* pass the ref to the Search button inside SearchBar */}
                        <SearchBar onSearch={handleSearch} inline actionRef={searchBtnRef} />
                    </div>

                    {/* give the What's New button a ref */}
                    <button
                        ref={whatsNewRef}
                        className="whats-new-btn"
                        type="button"
                        aria-label="What’s New"
                    >
                        <svg className="icon-globe" viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
                            <circle cx="12" cy="12" r="9" fill="none" stroke="currentColor" strokeWidth="1.6" />
                            <ellipse cx="12" cy="12" rx="5.5" ry="9" fill="none" stroke="currentColor" strokeWidth="1.2" />
                            <path d="M3 12h18M12 3a18 18 0 0 1 0 18" fill="none" stroke="currentColor" strokeWidth="1.2" />
                        </svg>
                        <span>What’s New</span>
                    </button>
                </div>
            </div>





            <div className="stage">
                <GlobeView
                    onPick={handleGlobePick}
                    focus={focus}
                    highlightIso2={
                        selected?.kind === 'country'
                            ? selected.iso2
                            : selected?.kind === 'city'
                                ? selected.countryIso2 ?? null
                                : null
                    }
                    cityMarker={
                        selected?.kind === 'city' && selected.lat && selected.lng
                            ? { lat: selected.lat, lng: selected.lng }
                            : null
                    }
                />
            </div>
            

            {overlayOpen && (
                <ArticleOverlay
                    items={data?.items ?? []}
                    total={total}
                    page={page}
                    pageSize={pageSize}
                    onPrev={goPrev}
                    onNext={goNext}
                    canPrev={canPrev}
                    canNext={canNext}
                    onClose={() => { setOverlayOpen(false); setError(null); }}
                    title={title ?? undefined}
                />
            )}

            {loading && <div className="loader">Loading…</div>}
            {!loading && error && <div className="loader" style={{ bottom: 60, color: '#f88' }}>{error}</div>}
        </div>
    );
}
