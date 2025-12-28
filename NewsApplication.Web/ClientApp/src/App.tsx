import { useCallback, useEffect, useState } from 'react';
import SearchBar, { type GeoPickContext } from './components/SearchBar';
import GlobeView from './components/GlobeView';
import ArticleOverlay from './components/ArticleOverlay';
import type { ArticleDto } from './types';
import { loadTopCitiesForHover } from './hoverCities';
import {
    resolveScope,
    reverseScope,
    searchArticles,
    prewarm,
    //preview,
    fetchTopCities,
    toArticleDto,
    type ResolveScopeResponse,
    type PreviewGeoCandidate
} from './api';

const UI_PAGE_SIZE = 6;

const whitespaceSplitter = /\s+/;

const formatLabel = (res: ResolveScopeResponse): string => {
    if (res.kind === 'city') {
        return res.countryIso2 ? `${res.label}, ${res.countryIso2}` : res.label;
    }
    return res.label;
};
function sanitizeCityKeywords(
    query: string | undefined,
    candidate: PreviewGeoCandidate
): string | undefined {
    const raw = (query ?? '').trim();
    if (!raw) return undefined;

    const tokens = raw.split(whitespaceSplitter).map(tok => tok.trim()).filter(Boolean);
    if (tokens.length === 0) return undefined;

    const iso2 = candidate.countryIso2?.toUpperCase() ?? '';
    const countryName = candidate.countryName ?? '';
    const shouldDropMacedonia = iso2 === 'MK' || /macedonia/i.test(countryName);

    const filtered = shouldDropMacedonia
        ? tokens.filter(tok => tok.trim().toLowerCase() !== 'macedonia')
        : tokens;

    if (filtered.length === 0) return undefined;

    return filtered.join(' ');
}

const isFiniteNumber = (value: number | null | undefined): value is number =>
    typeof value === 'number' && Number.isFinite(value);

type FocusHint = { lat: number; lng: number };
type ResolveOverrides = Partial<Pick<ResolveScopeResponse, 'countryIso2' | 'countryIso3' | 'focusLat' | 'focusLng'>>;

type CityPoint = {
    lat: number;
    lng: number;
    label?: string | null;
    id?: string | null;
    countryIso2?: string | null;
    countryIso3?: string | null;
    name?: string | null;
    population?: number | null;
};

type ScopeSel = {
    scopeKey: string;
    kind: ResolveScopeResponse['kind'];
    label: string;
    displayText?: string;
    countryIso2?: string;
    countryIso3?: string;
    cityId?: string;
    focusLat?: number;
    focusLng?: number;
};

export default function App() {
    const [searchSel, setSearchSel] = useState<ScopeSel | null>(null);
    const [globeSel, setGlobeSel] = useState<ScopeSel | null>(null);
    const [activeSource, setActiveSource] = useState<'searchbar' | 'globe'>('searchbar');
    const [overlayOpen, setOverlayOpen] = useState(false);
    const [currentScopeKey, setCurrentScopeKey] = useState<string | null>(null);
    const [page, setPage] = useState(1);

    const [items, setItems] = useState<ArticleDto[]>([]);
    const [total, setTotal] = useState<number | undefined>(undefined);
    const [canPrev, setCanPrev] = useState(false);
    const [canNext, setCanNext] = useState(false);
    const [nextProviderPage, setNextProviderPage] = useState<number | null>(null);
    const [focus, setFocus] = useState<{ lat: number; lng: number; altitude?: number } | null>(null);
    const [highlightIso2, setHighlightIso2] = useState<string | null>(null);

    const [cityMarker, setCityMarker] = useState<CityPoint | null>(null);
    const [cityMarkers, setCityMarkers] = useState<CityPoint[]>([]);
    const [hoverCities, setHoverCities] = useState<CityPoint[]>([]);

    const [hoverIso, setHoverIso] = useState<string | null>(null);
    const [hoveringHighlight, setHoveringHighlight] = useState(false);

    const normalize = useCallback(
        (s: string) => s.trim().toLowerCase().replace(/\s+/g, ' '),
        []
    );

    const load = useCallback(async (key: string, uiPage: number) => {
        const res = await searchArticles(key, uiPage);
        setCurrentScopeKey(key);
        setItems(res.items.map(toArticleDto));
        setPage(res.uiPage);
        setCanPrev(res.hasNewer);
        setCanNext(res.hasOlder);
        setTotal(res.totalDistinct);
        setNextProviderPage(res.prefetch?.providerPage ?? null);
    }, []);

    const syncVisuals = useCallback((sel: ScopeSel | null) => {
        if (!sel) {
            setHighlightIso2(null);
            setCityMarker(null);
            setCityMarkers([]);
            setFocus(null);
            return;
        }

        setHighlightIso2(sel.countryIso2 ?? null);
        const lat = sel.focusLat ?? null;
        const lng = sel.focusLng ?? null;
        const markerLabel = sel.displayText ?? sel.label;

        if ((sel.kind === 'city' || !!sel.cityId) && lat != null && lng != null) {
            setCityMarkers([]);
            setCityMarker({
                lat,
                lng,
                label: markerLabel,
                id: sel.cityId ?? undefined,
                countryIso2: sel.countryIso2 ?? undefined,
                countryIso3: sel.countryIso3 ?? undefined,
                name: sel.label
            });
            setFocus({ lat, lng, altitude: 1.6 });
            return;
        }

        setCityMarker(null);
        if (lat != null && lng != null) {
            setFocus({ lat, lng, altitude: 2.2 });
        } else {
            setFocus(null);
        }
    }, []);

    useEffect(() => {
        const activeSel = activeSource === 'searchbar' ? searchSel : globeSel;
        syncVisuals(activeSel);
    }, [activeSource, globeSel, searchSel, syncVisuals]);

    useEffect(() => {
        if (currentScopeKey && nextProviderPage != null) {
            prewarm(currentScopeKey, nextProviderPage).catch(() => { });
        }
    }, [currentScopeKey, nextProviderPage]);

    const buildSelection = useCallback(
        (res: ResolveScopeResponse, options?: { displayText?: string; focusHint?: FocusHint | null; overrides?: ResolveOverrides }): ScopeSel => {
            const focusLat = res.focusLat ?? options?.focusHint?.lat ?? options?.overrides?.focusLat;
            const focusLng = res.focusLng ?? options?.focusHint?.lng ?? options?.overrides?.focusLng;
            const label = formatLabel(res);

            return {
                scopeKey: res.scopeKey,
                kind: res.kind,
                label,
                displayText: options?.displayText,
                countryIso2: options?.overrides?.countryIso2 ?? res.countryIso2,
                countryIso3: options?.overrides?.countryIso3 ?? res.countryIso3,
                cityId: res.cityId,
                focusLat: focusLat ?? undefined,
                focusLng: focusLng ?? undefined
            };
        },
        []
    );

    const commitSelection = useCallback(
        async (sel: ScopeSel, source: 'searchbar' | 'globe', initialPage = 1) => {
            if (source === 'searchbar') {
                setSearchSel(sel);
            } else {
                setGlobeSel(sel);
            }
            setActiveSource(source);
            setOverlayOpen(true);
            setCityMarkers([]);
            syncVisuals(sel);
            await load(sel.scopeKey, initialPage);
        },
        [load, syncVisuals]
    );

    const resolveAndLoad = useCallback(
        async (
            body: Parameters<typeof resolveScope>[0],
            options: { focusHint?: FocusHint | null; overrides?: ResolveOverrides; displayText?: string; source: 'searchbar' | 'globe' }
        ) => {
            const res = await resolveScope(body);
            const overrides: ResolveOverrides = { ...(options?.overrides ?? {}) };

            if ('country' in body) {
                overrides.countryIso2 = overrides.countryIso2 ?? body.country.iso2?.toUpperCase();
                if (body.country.iso3) overrides.countryIso3 = overrides.countryIso3 ?? body.country.iso3.toUpperCase();
            }
            if ('city' in body) {
                overrides.countryIso2 = overrides.countryIso2 ?? body.city.countryIso2?.toUpperCase();
            }

            const sel = buildSelection(res, {
                focusHint: options?.focusHint ?? null,
                overrides,
                displayText: options?.displayText
            });

            await commitSelection(sel, options.source, 1);
        },
        [buildSelection, commitSelection]
    );

    async function onSearch(q: string, opts?: { countryIso2?: string }) {
        const trimmed = q.trim();
        if (!trimmed) return;

        try {
            const canReuse =
                searchSel &&
                searchSel.displayText &&
                normalize(searchSel.displayText) === normalize(trimmed);

            if (canReuse) {
                setActiveSource('searchbar');
                setOverlayOpen(true);
                await load(searchSel.scopeKey, 1);
                return;
            }

            const body: Parameters<typeof resolveScope>[0] =
                opts?.countryIso2
                    ? { q: trimmed, country: { iso2: opts.countryIso2.toUpperCase() } }
                    : { q: trimmed };
            await resolveAndLoad(body, { source: 'searchbar', displayText: trimmed });
        } catch (err) {
            console.error(err);
        }
    }

    function onAmbiguous(model: {
        outlineIso2?: string | null;
        cities: Array<{ lat: number; lng: number; label?: string }>;
        focus?: { lat: number; lng: number };
        label?: string;
    }) {
        setHighlightIso2(model.outlineIso2 ?? null);
        setCityMarker(null);
        setCityMarkers(model.cities.map(c => ({ lat: c.lat, lng: c.lng, label: c.label })));
        setFocus(model.focus ? { lat: model.focus.lat, lng: model.focus.lng, altitude: 2.0 } : null);
    }

    function onClearGeo() {
        syncVisuals(null);
    }

    function onClearSearch() {
        setSearchSel(null);
        if (activeSource === 'searchbar') {
            setOverlayOpen(false);
            setItems([]);
            setTotal(undefined);
            setCanPrev(false);
            setCanNext(false);
            setNextProviderPage(null);
            setCurrentScopeKey(null);
            setPage(1);
        } else {
            syncVisuals(globeSel);
        }
    }
    function onSearchEdit() {
        setSearchSel(null);
        setHighlightIso2(null);
        setCityMarker(null);
        setCityMarkers([]);
        setFocus(null);
        if (activeSource === 'searchbar') {
            setOverlayOpen(false);
            setItems([]);
            setTotal(undefined);
            setCanPrev(false);
            setCanNext(false);
            setNextProviderPage(null);
            setCurrentScopeKey(null);
            setPage(1);
        }
    }

    async function onPick(lat: number, lng: number) {
        try {
            const res = await reverseScope({ lat, lng });
            const sel = buildSelection(res, { focusHint: { lat, lng } });
            await commitSelection(sel, 'globe', 1);
        } catch (err) {
            console.error(err);
        }
    }

    async function onPickCountry(iso2: string, iso3: string | null, name: string | null, lat: number, lng: number) {
        const focusHint = Number.isFinite(lat) && Number.isFinite(lng) ? { lat, lng } : null;
        try {
            setCityMarker(null);
            setCityMarkers([]);
            await resolveAndLoad(
                { country: { iso2, iso3: iso3 ?? undefined, name: name ?? iso2 } },
                {
                    focusHint,
                    overrides: {
                        countryIso2: iso2?.toUpperCase(),
                        countryIso3: iso3?.toUpperCase()
                    },
                    source: 'globe'
                }
            );
        } catch (err) {
            console.error(err);
        }
    }

    const handleCommitCity = useCallback(
        (candidate: PreviewGeoCandidate, context?: GeoPickContext) => {
            if (!candidate?.id) return;

            const iso2 = candidate.countryIso2?.toUpperCase() ?? '';
            const lat = candidate.lat;
            const lng = candidate.lng;
            const focusHint = isFiniteNumber(lat) && isFiniteNumber(lng) ? { lat, lng } : null;

            const keywordTail = context?.keywordTail?.trim();
            const fullText = context?.fullText?.trim();
            const rawQueryText = keywordTail || fullText || undefined;
            const queryText = sanitizeCityKeywords(rawQueryText, candidate);

            const cityRequest = {
                city: {
                    id: candidate.id,
                    name: candidate.name,
                    countryIso2: iso2 || candidate.countryIso2 || ''
                },
                ...(queryText ? { q: queryText } : {})
            };

            resolveAndLoad(
                cityRequest,
                {
                    focusHint,
                    overrides: {
                        countryIso2: iso2 || undefined,
                        countryIso3: candidate.countryIso3?.toUpperCase()
                    },
                    displayText: context?.fullText ?? queryText ?? candidate.name ?? '',
                    source: 'searchbar'
                }
            ).catch(err => console.error(err));
        },
        [resolveAndLoad]
    );

    const handleCommitCountry = useCallback(
        (candidate: PreviewGeoCandidate, context?: GeoPickContext) => {
            const iso2 = (candidate.countryIso2 ?? candidate.id ?? '').toUpperCase();
            if (!iso2) return;

            const iso3 = candidate.countryIso3?.toUpperCase();
            const lat = candidate.lat;
            const lng = candidate.lng;
            const focusHint = isFiniteNumber(lat) && isFiniteNumber(lng) ? { lat, lng } : null;

            const keywordTail = context?.keywordTail?.trim();
            const fullText = context?.fullText?.trim();
            const queryText = keywordTail || fullText || undefined;

            const countryRequest = {
                country: {
                    iso2,
                    iso3: iso3 ?? undefined,
                    name: candidate.name ?? iso2
                },
                ...(queryText ? { q: queryText } : {})
            };

            resolveAndLoad(
                countryRequest,
                {
                    focusHint,
                    overrides: {
                        countryIso2: iso2,
                        countryIso3: iso3 ?? undefined
                    },
                    displayText: context?.fullText ?? queryText ?? candidate.name ?? '',
                    source: 'searchbar'
                }
            ).catch(err => console.error(err));
        },
        [resolveAndLoad]
    );

    const previewCitySelection = useCallback(
        (candidate: PreviewGeoCandidate) => {
            const iso2 = candidate.countryIso2?.toUpperCase() ?? null;
            setHighlightIso2(iso2);
            setCityMarkers([]);

            const lat = candidate.lat;
            const lng = candidate.lng;
            if (isFiniteNumber(lat) && isFiniteNumber(lng)) {
               const countryLabel = candidate.countryName ?? candidate.countryIso2 ?? undefined;
                const label = countryLabel ? `${candidate.name ?? ''}, ${countryLabel}`.trim().replace(/^,\s*/, '') : candidate.name ?? undefined;
                setCityMarker({ lat, lng, label });
                setFocus({ lat, lng, altitude: 1.6 });
            } else {
                setCityMarker(null);
            }
        },
        []
    );

    const previewCountrySelection = useCallback(
        (candidate: PreviewGeoCandidate) => {
            const iso2 = (candidate.countryIso2 ?? candidate.id ?? '').toUpperCase() || null;
            setHighlightIso2(iso2);
            setCityMarker(null);
            setCityMarkers([]);

            const lat = candidate.lat;
            const lng = candidate.lng;
            if (isFiniteNumber(lat) && isFiniteNumber(lng)) {
                setFocus({ lat, lng, altitude: 2.2 });
            } else {
                setFocus(null);
            }
        },
        []
    );

    const activeSel = activeSource === 'searchbar' ? searchSel : globeSel;

    const handleMarkerClick = useCallback((point?: CityPoint | null) => {
        if (!point) return;
        const focusLat = isFiniteNumber(point.lat) ? point.lat : undefined;
        const focusLng = isFiniteNumber(point.lng) ? point.lng : undefined;
        const focusHint = focusLat != null && focusLng != null ? { lat: focusLat, lng: focusLng } : null;
        const displayText = point.label ?? point.name ?? '';
        const countryIso2 = point.countryIso2?.toUpperCase();
        const countryIso3 = point.countryIso3?.toUpperCase();

        if (point.id && countryIso2) {
            resolveAndLoad(
                {
                    city: {
                        id: point.id,
                        name: point.name ?? displayText ?? '',
                        countryIso2
                    }
                },
                {
                    focusHint,
                    overrides: {
                        countryIso2,
                        countryIso3
                    },
                    displayText: displayText || point.name || '',
                    source: 'globe'
                }
            ).catch(err => console.error(err));
            return;
        }

        if (displayText) {
            void onSearch(displayText, countryIso2 ? { countryIso2 } : undefined);
        }
    }, [onSearch, resolveAndLoad]);

    const handleCountryHover = useCallback((iso2: string | null) => {
        const next = iso2 ? iso2.toUpperCase() : null;
        setHoverIso(next);
    }, []);

    useEffect(() => {
        let cancelled = false;
        const isSame = hoverIso && highlightIso2 && hoverIso.toUpperCase() === highlightIso2.toUpperCase();
        setHoveringHighlight(Boolean(isSame));
        const loadTopCities = async (iso2: string) => {
            try {
                const top = await loadTopCitiesForHover(iso2, fetchTopCities);
                if (!cancelled) setHoverCities(top);
            } catch (err) {
                if (!cancelled) setHoverCities([]);
                console.error(err);
            }
        };

        if (isSame && hoverIso) {
            void loadTopCities(hoverIso);
        } else {
            setHoverCities([]);
        }

        return () => { cancelled = true; };
    }, [hoverIso, highlightIso2]);

    return (
        <div className="app">
            <div className="topbar">
                <SearchBar
                    inline
                    value={searchSel?.displayText ?? searchSel?.label ?? ''}
                    onSearch={onSearch}
                    onPickCity={handleCommitCity}
                    onPickCountry={handleCommitCountry}
                    onPreviewCity={previewCitySelection}
                    onPreviewCountry={previewCountrySelection}
                    onAmbiguous={onAmbiguous}
                    onClearGeo={onClearGeo}
                    onClearSearch={onClearSearch}
                    onSearchEdit={onSearchEdit}
                />
            </div>

            <div className="stage">
                <div className="globe-wrap">
                    <GlobeView
                        onPick={onPick}
                        onPickCountry={onPickCountry}
                        focus={focus}
                        highlightIso2={highlightIso2}
                        cityMarker={cityMarker ?? (activeSel?.kind === 'city' ? cityMarker : null)}
                        cityMarkers={hoveringHighlight ? hoverCities : cityMarkers}
                        hoverCities={hoverCities}
                        onLabelClick={handleMarkerClick}
                        onCountryHover={handleCountryHover}
                    />
                </div>
            </div>

            {overlayOpen && activeSel && (
                <ArticleOverlay
                    title={activeSel.label || 'Articles'}
                    items={items}
                    total={total}
                    page={page}
                    pageSize={UI_PAGE_SIZE}
                    canPrev={canPrev}
                    canNext={canNext}
                    onPrev={() => { if (activeSel.scopeKey && page > 1) void load(activeSel.scopeKey, page - 1); }}
                    onNext={() => { if (activeSel.scopeKey) void load(activeSel.scopeKey, page + 1); }}
                    onClose={() => {
                        setOverlayOpen(false);
                    }}
                />
            )}
        </div>
    );
}
