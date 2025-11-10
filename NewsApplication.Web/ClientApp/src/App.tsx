import { useCallback, useEffect, useState } from 'react';
import SearchBar from './components/SearchBar';
import GlobeView from './components/GlobeView';
import ArticleOverlay from './components/ArticleOverlay';
import type { ArticleDto } from './types';
import {
    resolveScope,
    reverseScope,
    searchArticles,
    prewarm,
    toArticleDto,
    type ResolveScopeResponse,
    type PreviewGeoCandidate
} from './api';

const UI_PAGE_SIZE = 6;

const isFiniteNumber = (value: number | null | undefined): value is number =>
    typeof value === 'number' && Number.isFinite(value);

type FocusHint = { lat: number; lng: number };
type ResolveOverrides = Partial<Pick<ResolveScopeResponse, 'countryIso2' | 'countryIso3' | 'focusLat' | 'focusLng'>>;
export default function App() {
    const [scopeKey, setScopeKey] = useState<string | null>(null);
    const [label, setLabel] = useState<string>('');   // shown in SearchBar + overlay title
    const [page, setPage] = useState(1);

    const [items, setItems] = useState<ArticleDto[]>([]);
    const [total, setTotal] = useState<number | undefined>(undefined);
    const [canPrev, setCanPrev] = useState(false);
    const [canNext, setCanNext] = useState(false);
    const [nextProviderPage, setNextProviderPage] = useState<number | null>(null);
    const [focus, setFocus] = useState<{ lat: number; lng: number; altitude?: number } | null>(null);
    const [highlightIso2, setHighlightIso2] = useState<string | null>(null);
    const [cityMarker, setCityMarker] = useState<{ lat: number; lng: number } | null>(null);
    const [cityMarkers, setCityMarkers] = useState<Array<{ lat: number; lng: number }>>([]);


    // Load current UI page (6 items)

    const load = useCallback(async (key: string, uiPage: number) => {
        const res = await searchArticles(key, uiPage);
        setItems(res.items.map(toArticleDto));
        setPage(res.uiPage);
        setCanPrev(res.hasNewer);
        setCanNext(res.hasOlder);
        setTotal(res.totalDistinct);
        setNextProviderPage(res.prefetch?.providerPage ?? null);
    }, []);

    const applyResolved = useCallback(
        async (res: ResolveScopeResponse, initialPage = 1, options?: { focusHint?: FocusHint | null; overrides?: ResolveOverrides }) => {
            const effective: ResolveScopeResponse = {
                ...res,
                ...(options?.overrides ?? {})
            };

            setScopeKey(effective.scopeKey);
            setLabel(effective.label);
            setHighlightIso2(effective.countryIso2 ?? null);

            const hint = options?.focusHint ?? null;
            const lat = effective.focusLat ?? hint?.lat ?? null;
            const lng = effective.focusLng ?? hint?.lng ?? null;

            if (effective.kind === 'city' && lat != null && lng != null) {
                setCityMarker({ lat, lng });
                setFocus({ lat, lng, altitude: 1.6 });
            } else {
                setCityMarker(null);
                if (lat != null && lng != null) {
                    setFocus({ lat, lng, altitude: 2.2 });
                } else {
                    setFocus(null);
                }
            }

            await load(effective.scopeKey, initialPage);
        },
        [load]
    );

    // Keep one provider page ahead warm (background)
    useEffect(() => {
        if (scopeKey && nextProviderPage != null) {
            prewarm(scopeKey, nextProviderPage).catch(() => { });
        }
    }, [scopeKey, nextProviderPage]);

    const resolveAndLoad = useCallback(
        async (body: Parameters<typeof resolveScope>[0], options?: { focusHint?: FocusHint | null; overrides?: ResolveOverrides }) => {
            const res = await resolveScope(body);
            const overrides: ResolveOverrides = { ...(options?.overrides ?? {}) };

            if ('country' in body) {
                overrides.countryIso2 = overrides.countryIso2 ?? body.country.iso2?.toUpperCase();
                if (body.country.iso3) overrides.countryIso3 = overrides.countryIso3 ?? body.country.iso3.toUpperCase();
            }
            if ('city' in body) {
                overrides.countryIso2 = overrides.countryIso2 ?? body.city.countryIso2?.toUpperCase();
            }

            await applyResolved(res, 1, { focusHint: options?.focusHint ?? null, overrides });
        },
        [applyResolved]
    );

    // SearchBar submit → real search (unambiguous or composite keyword-only)
    async function onSearch(q: string) {
        const trimmed = q.trim();
        if (!trimmed) return;

        try {
            // Clear any map-only ambiguity state (multi pins) before doing a real search
            setCityMarkers([]);          // <-- new: remove batch markers from CityInCountry preview
            // (No need to clear highlightIso2/cityMarker here; resolveAndLoad will set them.)

            await resolveAndLoad({ q: trimmed });
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
        setCityMarkers(model.cities.map(c => ({ lat: c.lat, lng: c.lng })));
        setFocus(model.focus ? { lat: model.focus.lat, lng: model.focus.lng, altitude: 2.0 } : null);
    }


    function onClearGeo() {
        // Composite → we do NOT show geo, so clear any previous outline/pins
        setHighlightIso2(null);
        setCityMarker(null);
        setCityMarkers([]);
        setFocus(null);
    }

    // Globe clicks
    async function onPick(lat: number, lng: number) {
        try {
            const res = await reverseScope({ lat, lng });
            await applyResolved(res, 1);
        } catch (err) {
            console.error(err);
        }
    }

    // Country polygon click (now passes iso2 + iso3)
    async function onPickCountry(iso2: string, iso3: string | null, name: string | null, lat: number, lng: number) {
        const focusHint = Number.isFinite(lat) && Number.isFinite(lng) ? { lat, lng } : null;
        try {
            await resolveAndLoad(
                { country: { iso2, iso3: iso3 ?? undefined, name: name ?? iso2 } },
                {
                    focusHint,
                    overrides: {
                        countryIso2: iso2?.toUpperCase(),
                        countryIso3: iso3?.toUpperCase()
                    }
                }
            );
        } catch (err) {
            console.error(err);
        }
    }

    const handlePreviewCity = useCallback(
        (candidate: PreviewGeoCandidate, keywords?: string) => {
            if (!candidate?.id) return;

            const iso2 = candidate.countryIso2?.toUpperCase() ?? '';
            const lat = candidate.lat;
            const lng = candidate.lng;
            const focusHint = isFiniteNumber(lat) && isFiniteNumber(lng) ? { lat, lng } : null;

            // Build the city request with keywords if present
            const cityRequest = {
                city: {
                    id: candidate.id,
                    name: candidate.name,
                    countryIso2: iso2 || candidate.countryIso2 || ''
                },
                // Add keywords to the request if they exist
                ...(keywords ? { q: keywords } : {})
            };

            resolveAndLoad(
                cityRequest,
                {
                    focusHint,
                    overrides: {
                        countryIso2: iso2 || undefined,
                        countryIso3: candidate.countryIso3?.toUpperCase()
                    }
                }
            ).catch(err => console.error(err));
        },
        [resolveAndLoad]
    );

    const handlePreviewCountry = useCallback(
        (candidate: PreviewGeoCandidate, keywords?: string) => {
            const iso2 = (candidate.countryIso2 ?? candidate.id ?? '').toUpperCase();
            if (!iso2) return;

            const iso3 = candidate.countryIso3?.toUpperCase();
            const lat = candidate.lat;
            const lng = candidate.lng;
            const focusHint = isFiniteNumber(lat) && isFiniteNumber(lng) ? { lat, lng } : null;

            // Build the country request with keywords if present
            const countryRequest = {
                country: {
                    iso2,
                    iso3: iso3 ?? undefined,
                    name: candidate.name ?? iso2
                },
                // Add keywords to the request if they exist
                ...(keywords ? { q: keywords } : {})
            };

            resolveAndLoad(
                countryRequest,
                {
                    focusHint,
                    overrides: {
                        countryIso2: iso2,
                        countryIso3: iso3 ?? undefined
                    }
                }
            ).catch(err => console.error(err));
        },
        [resolveAndLoad]
    );
    useEffect(() => {
        // TEMP: force a visible outline + pin 2s after load
        const t = setTimeout(() => {
            setHighlightIso2('PH'); // Philippines — should outline in lime
            setCityMarker({ lat: 14.5995, lng: 120.9842 }); // Manila
        }, 2000);
        return () => clearTimeout(t);
    }, []);

    return (
        <div className="app">
            <div className="topbar">
                <SearchBar
                    inline
                    value={label}
                    onSearch={onSearch}
                    onPickCity={handlePreviewCity}
                    onPickCountry={handlePreviewCountry}
                    onAmbiguous={onAmbiguous}   // show geo for ambiguous
                    onClearGeo={onClearGeo}     // clear geo for composite
                />
            </div>

            <div className="stage">
                <div className="globe-wrap">
                    <GlobeView
                        onPick={onPick}
                        onPickCountry={onPickCountry}
                        focus={focus}
                        highlightIso2={highlightIso2}
                        cityMarker={cityMarker}
                        cityMarkers={cityMarkers} 
                    />
                </div>
            </div>

            {scopeKey && (
                <ArticleOverlay
                    title={label || 'Articles'}
                    items={items}
                    total={total}
                    page={page}
                    pageSize={UI_PAGE_SIZE}
                    canPrev={canPrev}
                    canNext={canNext}
                    onPrev={() => { if (scopeKey && page > 1) void load(scopeKey, page - 1); }}
                    onNext={() => { if (scopeKey) void load(scopeKey, page + 1); }}
                    onClose={() => {
                        setScopeKey(null);
                        setItems([]);
                        setTotal(undefined);
                        setCanPrev(false);
                        setCanNext(false);
                        setNextProviderPage(null);
                    
                    }}
                />
            )}
        </div>
    );
}
