import {
    useEffect,
    useRef,
    useState,
    type ChangeEvent,
    type FormEvent,
    type MouseEvent,
    type Ref,
} from 'react';
import { preview, ScopeKind, type PreviewGeoCandidate, type PreviewResponse } from '../api';

interface Props {
    onSearch: (q: string) => void;
    onPickCity?: (city: PreviewGeoCandidate) => void;
    onPickCountry?: (country: PreviewGeoCandidate) => void;
    inline?: boolean;
    actionRef?: Ref<HTMLButtonElement>;
    value?: string; // controlled text from parent (e.g., "Skopje")
    onAmbiguous?: (model: {
        outlineIso2?: string | null;
        cities: Array<{ lat: number; lng: number; label?: string }>;
        focus?: { lat: number; lng: number };
        label?: string;
    }) => void;
    onClearGeo?: () => void;
}

const PREVIEW_DEBOUNCE_MS = 220;

// util: stable uppercase
const up = (s?: string | null) => (s ?? '').toUpperCase();


// util: dedupe by key
function dedupeBy<T>(arr: T[], key: (x: T) => string) {
    const seen = new Set<string>();
    const out: T[] = [];
    for (const x of arr) {
        const k = key(x);
        if (!k) continue;
        if (!seen.has(k)) {
            seen.add(k);
            out.push(x);
        }
    }
    return out;
}

export default function SearchBar({
    onSearch,
    onPickCity,
    onPickCountry,
    onAmbiguous,
    inline = false,
    actionRef,
    value,
    onClearGeo,
}: Props) {
    const [q, setQ] = useState(value ?? '');
    const [dirty, setDirty] = useState(false);
    const [previewResult, setPreviewResult] = useState<PreviewResponse | null>(null);
    const [focused, setFocused] = useState(false);
    const [isPreviewing, setIsPreviewing] = useState(false);
    const clearGeo = () => { onClearGeo?.(); };
    const trimmed = q.trim();
    const blurTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
    // ...existing state
    const [active, setActive] = useState(-1);

    // NEW
    const [pendingPick, setPendingPick] =
        useState<null | { t: 'city' | 'country'; v: PreviewGeoCandidate; k: string }>(null);
    const inputRef = useRef<HTMLInputElement>(null);
    const refocusNoScroll = () => inputRef.current?.focus({ preventScroll: true });
    // keep local state in sync with parent-controlled value
    useEffect(() => {
        if (typeof value === 'string') {
            setQ(value);
            setDirty(false);
            setPreviewResult(null);
        }
    }, [value]);

    // Replace the entire value sync useEffect with this simpler version:
    const hasInteractedRef = useRef(false);

    useEffect(() => {
        // Only sync from parent if the user hasn't started interacting yet
        if (!hasInteractedRef.current && typeof value === 'string') {
            setQ(value);
            setDirty(false);
            setPreviewResult(null);
        }
    }, [value]);

    const handleChange = (e: ChangeEvent<HTMLInputElement>) => {
        hasInteractedRef.current = true;  // ← ADD THIS
        clearGeo();
        setPendingPick(null);
        setDirty(true);
        setQ(e.target.value);
        setFocused(true);
    };
  
    useEffect(
        () => () => {
            if (blurTimer.current !== null) {
                clearTimeout(blurTimer.current);
                blurTimer.current = null;
            }
        },
        []
    );

    // fetch preview (debounced)
    useEffect(() => {
        if (!dirty || trimmed.length === 0) {
            setIsPreviewing(false);
            if (!dirty) setPreviewResult(null);
            return;
        }

        let cancelled = false;
        setIsPreviewing(true);
        const timer = setTimeout(async () => {
            try {
                const res = await preview(trimmed);
                if (!cancelled) setPreviewResult(res);
            } catch (err) {
                if (!cancelled) {
                    console.error(err);
                    setPreviewResult(null);
                }
            } finally {
                if (!cancelled) setIsPreviewing(false);
            }
        }, PREVIEW_DEBOUNCE_MS);

        return () => {
            cancelled = true;
            clearTimeout(timer);
        };
    }, [trimmed, dirty]);

    const clearBlurTimer = () => {
        if (blurTimer.current !== null) {
            clearTimeout(blurTimer.current);
            blurTimer.current = null;
        }
    };

    const handleFocus = () => {
        clearBlurTimer();
        setFocused(true);
        refocusNoScroll(); 
    };

    const handleBlur = () => {
        clearBlurTimer();
        blurTimer.current = setTimeout(() => {
            setFocused(false);
        }, 120);
    };


    const handleSuggestionMouseDown = (e: MouseEvent<HTMLButtonElement>) => {
        // prevents input blur before click
        e.preventDefault();
        clearBlurTimer();
    };

    const selectCity = (candidate: PreviewGeoCandidate) => {
        if (!candidate?.id) return;
        const tail = capturedKeywords(previewResult);                 // NEW: capture before clearing
        setPendingPick({ t: 'city', v: candidate, k: tail });
        setDirty(false);
        setPreviewResult(null);
        // was: setQ(candidate.name ?? '');
        setQ(withKeywords(candidate.name ?? '', previewResult)); // ← keep non-geo in input
        setFocused(true);
        refocusNoScroll();
    };

    const selectCountry = (candidate: PreviewGeoCandidate) => {
        if (!candidate?.id) return;
        const tail = capturedKeywords(previewResult);                 // NEW
        setPendingPick({ t: 'country', v: candidate, k: tail });    
        setDirty(false);
        setPreviewResult(null);
        // was: setQ(candidate.name ?? '');
        setQ(withKeywords(candidate.name ?? '', previewResult)); // ← keep non-geo in input
        setFocused(true);
        refocusNoScroll();
    };


    const submit = async (e: FormEvent<HTMLFormElement>) => {
        e.preventDefault();
        const text = trimmed;
        hasInteractedRef.current = true;
        if (!text && !pendingPick) return;

        // AUTO-SELECT: If suggestions are showing and user didn't pick one, auto-select the first
        if (!pendingPick && showSuggestions && (cityPills.length > 0 || countryPills.length > 0)) {
            const tail = capturedKeywords(previewResult);

            const candidate = cityPills.length > 0
                ? { t: 'city' as const, v: cityPills[0] }
                : { t: 'country' as const, v: countryPills[0] };

            const { t, v } = candidate;
            const nextQ = tail ? `${v.name ?? ''} ${tail}`.trim() : (v.name ?? '');

            clearGeo();
            setDirty(false);
            setPreviewResult(null);
            setQ(nextQ);

            if (t === 'city') onPickCity?.(v);
            else onPickCountry?.(v);

            inputRef.current?.focus();
            refocusNoScroll();
            setFocused(true);
            return;
        }

        // If a pill was staged (via keyboard Enter), commit it and stop.
        if (pendingPick) {
            clearGeo();
            const { t, v, k } = pendingPick;
            const nextQ = k ? `${v.name ?? ''} ${k}`.trim() : (v.name ?? '');
            setPendingPick(null);
            setDirty(false);
            setPreviewResult(null);
            setQ(nextQ);

            if (t === 'city') onPickCity?.(v);
            else onPickCountry?.(v);

            inputRef.current?.focus();
            refocusNoScroll();
            setFocused(true);
            return;
        }


        // Always work with a *fresh* preview so heuristics are reliable
        const pr: PreviewResponse | null = previewResult ?? (await preview(text).catch(() => null));
        if (!pr) {
            // no preview → treat as plain keyword search
            onClearGeo?.();
            onSearch(text);
            inputRef.current?.focus(); refocusNoScroll(); setFocused(true);
            return;
        }

        // Always base ambiguity on *cityMatches* (targets may be collapsed already)
        const cities = (pr.cityMatches ?? [])
            .filter(c => Number.isFinite(Number(c.lat)) && Number.isFinite(Number(c.lng)));

        // --- A) Server-declared ambiguous kinds ---
        if (pr.kind === ScopeKind.Composite && pr.isAmbiguous) {
            const chosen =
                pr.outlineIso2 ??
                (pr.diagnostics && (pr.diagnostics['chosenIso2'] as string | undefined)) ??
                null;

            if (chosen) {
                // We have a country signal → let server resolve to in-country city and keep keywords
                onClearGeo?.();
                setDirty(false); setPreviewResult(null);
                onSearch(text); // proceed to resolveScope + articles
                inputRef.current?.focus(); refocusNoScroll(); setFocused(true);
                return;
            }

            // True cross-country ambiguity → show pins and stop
            clearGeo();
            const pts = (pr.cityMatches ?? [])
                .filter(c => Number.isFinite(Number(c.lat)) && Number.isFinite(Number(c.lng)))
                .map(c => ({ lat: Number(c.lat), lng: Number(c.lng) }));

            onAmbiguous?.({
                outlineIso2: null,
                cities: pts.map(p => ({ ...p })), // you already map to {lat,lng,label}; keep that if you prefer
                focus: pts.length
                    ? {
                        lat: pts.reduce((s, p) => s + p.lat, 0) / pts.length,
                        lng: pts.reduce((s, p) => s + p.lng, 0) / pts.length,
                    }
                    : undefined,
                label: text,
            });

            setDirty(false); setPreviewResult(null);
            inputRef.current?.focus(); refocusNoScroll(); setFocused(true);
            return;
        }

        if (pr.kind === ScopeKind.CityInCountry && pr.isAmbiguous) {
            clearGeo(); 
            const iso = pr.outlineIso2 ?? (pr.diagnostics && (pr.diagnostics['chosenIso2'] as string | undefined)) ?? null;
            const inCountry = iso
                ? cities.filter(c => (c.countryIso2 ?? '').toUpperCase() === iso.toUpperCase())
                : cities;

            onAmbiguous?.({
                outlineIso2: iso ?? null,
                cities: inCountry.map(c => ({ lat: Number(c.lat), lng: Number(c.lng), label: c.name ?? undefined })),
                focus: inCountry.length ? {
                    lat: inCountry.reduce((s, p) => s + Number(p.lat), 0) / inCountry.length,
                    lng: inCountry.reduce((s, p) => s + Number(p.lng), 0) / inCountry.length
                } : undefined,
                label: text
            });
            setDirty(false); setPreviewResult(null);
            inputRef.current?.focus(); refocusNoScroll(); setFocused(true);
            return; // ← no onSearch
        }

        // --- B) Client-side ambiguity: multiple exact city hits across countries ---
        const EXACT = 0.999;
        // group by *name* and check distinct ISO2s among near-1.0 hits
        const exactByName = new Map<string, Array<typeof cities[number]>>();
        for (const c of cities) {
            const sc = typeof c.score === 'number' ? c.score : 0;
            if (sc >= EXACT) {
                const key = (c.name ?? '').trim().toLowerCase();
                if (!key) continue;
                const arr = exactByName.get(key) ?? [];
                arr.push(c);
                exactByName.set(key, arr);
            }
        }
        let compositePins: Array<{ lat: number; lng: number; label?: string }> | null = null;
        for (const [, arr] of exactByName) {
            const isoCount = new Set(arr.map(x => (x.countryIso2 ?? '').toUpperCase()).filter(Boolean)).size;
            if (isoCount >= 2) {
                compositePins = dedupeBy(arr, x => x.id ?? `${x.name}|${up(x.countryIso2)}`)
                    .map(c => ({ lat: Number(c.lat), lng: Number(c.lng), label: c.name ?? undefined }));
                break;
            }
        }
        if (compositePins && compositePins.length >= 2) {
            clearGeo(); 
            onAmbiguous?.({
                outlineIso2: null, // cross-country → no outline
                cities: compositePins,
                focus: {
                    lat: compositePins.reduce((s, p) => s + p.lat, 0) / compositePins.length,
                    lng: compositePins.reduce((s, p) => s + p.lng, 0) / compositePins.length
                },
                label: text
            });
            setDirty(false); setPreviewResult(null);
            inputRef.current?.focus(); refocusNoScroll(); setFocused(true);
            return; // ← no onSearch
        }

        // --- C) Composite but not ambiguous → keyword-only search (drop geo) ---
        if (pr.kind === ScopeKind.Composite && !pr.isAmbiguous) {
            const tail = capturedKeywords(pr);
            const base = bestLabelFromPreview(pr) ?? text;      // show normalized geo if we have it
            setQ(tail ? `${base} ${tail}`.trim() : base);
            const kw = (pr.nonGeoKeywords ?? []).join(' ').trim();
            onClearGeo?.();
            setDirty(false); setPreviewResult(null);
            onSearch(kw || text);
            inputRef.current?.focus(); refocusNoScroll(); setFocused(true);
            return;
        }

        // --- D) Otherwise proceed with a real search ---
        const tail = capturedKeywords(pr);
        const base = bestLabelFromPreview(pr) ?? text;
        setQ(tail ? `${base} ${tail}`.trim() : base);

        onClearGeo?.();
        setDirty(false); setPreviewResult(null);
        onSearch(text);
        inputRef.current?.focus(); refocusNoScroll(); setFocused(true);
        return;
    };
  

    function withKeywords(base: string, pr: PreviewResponse | null) {
        const kws = (pr?.nonGeoKeywords ?? []).filter(Boolean);
        if (!kws.length) return base.trim();
        return `${base} ${kws.join(' ')}`.trim();
    }
    function capturedKeywords(pr: PreviewResponse | null) {
        return (pr?.nonGeoKeywords ?? []).filter(Boolean).join(" ");
    }

    function labelFor(x: PreviewGeoCandidate) {
        const iso = (x.countryIso2 ?? x.countryIso3 ?? "").trim();
        return iso ? `${x.name ?? ""}, ${up(iso)}` : (x.name ?? "");
    }

    function bestLabelFromPreview(pr: PreviewResponse | null): string | null {
        if (!pr) return null;

        // Prefer a city; bias to a server-chosen country if present
        const chosenIso = up(
            (pr.outlineIso2 ??
                (pr.diagnostics && (pr.diagnostics["chosenIso2"] as string | undefined)) ??
                "") || ""
        );

        const cities = pr.cityMatches ?? [];
        const topCity =
            (chosenIso && cities.find(c => up(c.countryIso2 ?? "") === chosenIso)) ||
            (cities.length ? cities[0] : null);

        if (topCity?.name) return labelFor(topCity);

        // Fall back to a country
        const countries = pr.countryMatches ?? [];
        if (countries.length) return labelFor(countries[0]);

        return null;
    }

    // ----- Build pills (API + synthesized) --------------------------------------

    // From API
    const apiCountryPills = previewResult?.countryMatches ?? [];
    const cityPills = previewResult?.cityMatches ?? [];

    // Display helpers
    const countryNameOf = (c: PreviewGeoCandidate) =>
        (c.countryName ?? '').trim();
    const countryIsoOf = (c: PreviewGeoCandidate) =>
        up(c.countryIso2 ?? c.countryIso3);

    // Synthesize countries from city matches (for the Countries group)
    const countriesFromCitiesRaw: PreviewGeoCandidate[] = cityPills
        .map((c) => {
            const name = countryNameOf(c);
            const iso = countryIsoOf(c);
            if (!name) return null;
            return {
                id: iso || up(name), // stable id (prefer ISO)
                name,                 // country display name
                countryIso2: iso || undefined,
            } as PreviewGeoCandidate;
        })
        .filter(Boolean) as PreviewGeoCandidate[];

    // Merge & dedupe
    const countryPills = dedupeBy(
        [...apiCountryPills, ...countriesFromCitiesRaw],
        (x) => (x.countryIso2 ? up(x.countryIso2) : up(x.id || x.name))
    ).slice(0, 8); // keep list tight


    // reset the active index whenever the suggestion set changes
    

    useEffect(() => {
        setActive(-1);
    }, [cityPills.length, countryPills.length, trimmed]);

    const hasSuggestions = countryPills.length > 0 || cityPills.length > 0;
    const showSuggestions = focused && dirty && trimmed.length > 0 && hasSuggestions;
    const showAmbiguity = dirty && previewResult !== null && !previewResult.canSearch;

 

    function onKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
        if (!showSuggestions) return;
        const total = countryPills.length + cityPills.length;
        if (total === 0) return;

        if (e.key === 'ArrowDown') {
            e.preventDefault();
            setActive(i => (i + 1) % total);
            return;
        }
        if (e.key === 'ArrowUp') {
            e.preventDefault();
            setActive(i => (i - 1 + total) % total);
            return;
        }

        if (e.key === 'Enter') {
            // Stage a pick only if the user actually navigated
            if (active >= 0) {
                const all = [
                    ...countryPills.map((c) => ({ t: 'country' as const, v: c })),
                    ...cityPills.map((c) => ({ t: 'city' as const, v: c })),
                ];
                const pick = all[active];
                if (pick) {
                    const tail = capturedKeywords(previewResult);  // ← ADD THIS LINE
                    setPendingPick({ ...pick, k: tail });  // ← SPREAD pick and add k property
                }
            }
            // Let the form submit to go through submit()
        }
    }

    return (
        <div className={`searchbar ${inline ? 'searchbar--inline' : ''} ${showSuggestions ? 'is-open' : ''}`}>
            <form onSubmit={submit} className="searchbar__form">
                <div className="searchbar__field">
                    <input
                        ref={inputRef}
                        type="search"
                        placeholder="Search a country or city..."
                        value={q}
                        onChange={handleChange}
                        onFocus={handleFocus}
                        onBlur={handleBlur}
                        onKeyDown={onKeyDown}
                        aria-autocomplete="list"
                        aria-expanded={showSuggestions}
                    />
                    {q && (
                        <button
                            type="button"
                            className="searchbar__clear"
                            aria-label="Clear"
                            onMouseDown={(e) => e.preventDefault()}   // keep focus
                            onClick={() => {
                                clearGeo(); 
                                setQ('');
                                setDirty(false);
                                setPreviewResult(null);
                            }}
                        >
                            ×
                        </button>
                    )}

                    {showSuggestions && (
                        <div className="searchbar__panel" role="listbox">
                            {/* Countries (API + inferred from city matches) */}
                            {countryPills.length > 0 && (
                                <div className="searchbar__group">
                                    <div className="searchbar__group-title">Countries</div>
                                    <ul className="searchbar__list">
                                        {countryPills.map((country, idx) => {
                                            const code = countryIsoOf(country);
                                            const globalIndex = idx; // first block
                                            const isActive = active === globalIndex;
                                            return (
                                                <li key={`country-${country.id}`}>
                                                    <button
                                                        type="button"
                                                        role="option"
                                                        aria-selected={isActive}
                                                        className={`searchbar__row ${isActive ? 'is-active' : ''}`}
                                                        onMouseDown={handleSuggestionMouseDown}
                                                        onMouseEnter={() => setActive(globalIndex)}
                                                        onClick={() => selectCountry(country)}
                                                    >
                                                        <span className="searchbar__row-main">{country.name}</span>
                                                        {code && <span className="searchbar__chip">{code}</span>}
                                                    </button>
                                                </li>
                                            );
                                        })}
                                    </ul>
                                </div>
                            )}

                            {/* Cities (show country NAME under the city; ISO2 chip on right) */}
                            {cityPills.length > 0 && (
                                <div className="searchbar__group">
                                    <div className="searchbar__group-title">Cities</div>
                                    <ul className="searchbar__list">
                                        {cityPills.map((city, idx) => {
                                            const globalIndex = countryPills.length + idx;
                                            const isActive = active === globalIndex;
                                            const cName = countryNameOf(city);
                                            const cIso = countryIsoOf(city);
                                            return (
                                                <li key={`city-${city.id}`}>
                                                    <button
                                                        type="button"
                                                        role="option"
                                                        aria-selected={isActive}
                                                        className={`searchbar__row ${isActive ? 'is-active' : ''}`}
                                                        onMouseDown={handleSuggestionMouseDown}
                                                        onMouseEnter={() => setActive(globalIndex)}
                                                        onClick={() => selectCity(city)}
                                                    >
                                                        <span className="searchbar__row-col">
                                                            <span className="searchbar__row-main">{city.name}</span>
                                                            {cName && <span className="searchbar__row-sub">{cName}</span>}
                                                        </span>
                                                        {cIso && <span className="searchbar__chip">{cIso}</span>}
                                                    </button>
                                                </li>
                                            );
                                        })}
                                    </ul>
                                </div>
                            )}
                        </div>
                    )}
                </div>
                <button
                    ref={actionRef}
                    type="submit"
                    className="searchbar__submit"
                >
                    {isPreviewing && dirty ? 'Searching…' : 'Search'}
                </button>
            </form>

            {showAmbiguity && (
                <div className="searchbar__status">
                    {previewResult?.kind === ScopeKind.CityInCountry
                        ? 'Select a city to continue'
                        : 'Refine your search to choose a location'}
                </div>
            )}
        </div>
    );
}
