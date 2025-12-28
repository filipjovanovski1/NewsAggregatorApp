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

export type GeoPickContext = {
    fullText: string;
    keywordTail?: string;
};
interface Props {
    onSearch: (q: string, opts?: { countryIso2?: string }) => void;
    onPickCity?: (city: PreviewGeoCandidate, context?: GeoPickContext) => void;
    onPickCountry?: (country: PreviewGeoCandidate, context?: GeoPickContext) => void;
    onPreviewCity?: (city: PreviewGeoCandidate, keywords?: string) => void;
    onPreviewCountry?: (country: PreviewGeoCandidate, keywords?: string) => void;
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
    onClearSearch?: () => void;
    onSearchEdit?: () => void;
}

//THIS SEGMENT IS FOR THE CHECK

type SearchBarFlow = { flowId: string; code: string; text?: string; extra?: unknown };

declare global {
    interface Window {
        __SB_FLOW?: SearchBarFlow;
        __SB_FLOW_ID?: string;
    }
}

//THIS SEGMENT IS FOR THE CHECK

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
    onPreviewCity,
    onPreviewCountry,
    onAmbiguous,
    inline = false,
    actionRef,
    value,
    onClearGeo,
    onClearSearch,
    onSearchEdit,
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
        useState<
            | null
            | {
                t: 'city' | 'country';
                v: PreviewGeoCandidate;
                k: string;
                text: string;
            }
        >(null);
    const inputRef = useRef<HTMLInputElement>(null);
    const refocusNoScroll = () => inputRef.current?.focus({ preventScroll: true });

    // Replace the entire value sync useEffect with this simpler version:
    const hasInteractedRef = useRef(false);

    useEffect(() => {
        if (!hasInteractedRef.current && typeof value === 'string') {
            setQ(value);
            setDirty(false);
            setPreviewResult(null);
        }
    }, [value]);

    const handleChange = (e: ChangeEvent<HTMLInputElement>) => {
        hasInteractedRef.current = true;  // ← ADD THIS
        onSearchEdit?.();
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
        const tail = capturedKeywords(previewResult);
        const phrase = cityPhrase(candidate);
        const { text, keywordTail: prunedTail } = applyGeoSuggestion(trimmed, phrase, tail, candidate);
        const nextTail = prunedTail ?? '';

        setPendingPick({ t: 'city', v: candidate, k: nextTail, text });
        onPreviewCity?.(candidate, prunedTail);
        setDirty(false);
        setPreviewResult(null);
        // was: setQ(candidate.name ?? '');
        setQ(text);
        setFocused(true);
        refocusNoScroll();
    };

    const selectCountry = (candidate: PreviewGeoCandidate) => {
        if (!candidate?.id) return;
        const tail = capturedKeywords(previewResult);
        const phrase = countryPhrase(candidate);
        const { text, keywordTail: prunedTail } = applyGeoSuggestion(trimmed, phrase, tail, candidate);
        const nextTail = prunedTail ?? '';

        setPendingPick({ t: 'country', v: candidate, k: nextTail, text });
        onPreviewCountry?.(candidate, prunedTail);     // NEW
        setDirty(false);
        setPreviewResult(null);
        // was: setQ(candidate.name ?? '');
        setQ(text);
        setFocused(true);
        refocusNoScroll();
    };


    // In SearchBar.tsx, replace the submit function with this improved version:

    const submit = async (e: FormEvent<HTMLFormElement>) => {
        e.preventDefault();
        let text = trimmed;
        hasInteractedRef.current = true;

        const flowId = crypto.randomUUID();         //CHECK
        type FlowExtra = Record<string, unknown> | undefined;

        const flag = (code: string, extra?: FlowExtra) => {           //CHECK
            const payload = { flowId, code, text, trimmed, extra };     //CHECK

            console.debug("[SearchBar.submit]", payload);       //CHECK

            window.__SB_FLOW = payload;        //CHECK
            window.__SB_FLOW_ID = flowId;     //CHECK

            return payload;         //CHECK
        };

        flag("START", { hasPendingPick: !!pendingPick });               //CHECK

        if (!text && !pendingPick) {                
            flag("EXIT_EMPTY");                     //CHECK
            return;                                 
        }


        // If a pill was staged (via keyboard Enter), commit it before continuing.
        if (pendingPick) {
            const { t, v, k, text: stagedText } = pendingPick;
            const nextQ = stagedText.trim();
            setPendingPick(null);
            setDirty(false);
            setPreviewResult(null);
            setQ(nextQ);
            text = nextQ;

            const keywordTail = k.trim().length ? k.trim() : undefined;
            const context: GeoPickContext = {
                fullText: nextQ,
                ...(keywordTail ? { keywordTail } : {}),
            };

            flag("PENDING_PICK_COMMIT", { t, nextQ });          //CHECK

            if (t === "city") {                                                                                             //CHECK
                flag("CALL_onPickCity", { id: v.id ?? null, name: v.name ?? null, iso2: v.countryIso2 ?? null });           //CHECK
            } else {                                                                                                        //CHECK
                flag("CALL_onPickCountry", { id: v.id ?? null, name: v.name ?? null, iso2: v.countryIso2 ?? null });        //CHECK
            }



            if (t === 'city') onPickCity?.(v, context);
            else onPickCountry?.(v, context);

            inputRef.current?.focus();
            refocusNoScroll();
            setFocused(true);
            return;
        }

        // IMPORTANT: Don't clear geo state at the start - let the specific handlers decide
        // clearGeo(); // <- REMOVE THIS

        //CHECK
        flag("PREVIEW_BEGIN", { usingCached: !!previewResult });          
        const pr: PreviewResponse | null =
            previewResult ?? (await preview(text).catch(() => null));

        flag(
            "PREVIEW_DONE",
            pr
                ? {
                    kind: pr.kind,
                    isAmbiguous: pr.isAmbiguous,
                    outlineIso2: pr.outlineIso2,
                    cityMatches: pr.cityMatches?.length ?? 0,
                }
                : undefined
        );
         //CHECK

        if (!pr) {
            // no preview → treat as plain keyword search (keep geo state intact)
            flag("NO_PREVIEW_PLAIN_SEARCH");                    //CHECK
            onSearch(text);
            inputRef.current?.focus();
            refocusNoScroll();
            setFocused(true);
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
                flag("COMPOSITE_AMBIGUOUS_CHOSEN", { chosen });         //CHECK

                // Server chose a country - do the search WITHOUT clearing geo
                // (Keep the current highlight/marker state)
                setDirty(false);
                setPreviewResult(null);

                const base = bestLabelFromPreview(pr) ?? text;
                const nextDisplay = mergeSuggestion(text, base, capturedKeywords(pr));
                setQ(nextDisplay);

                const safeQ = sanitizeQueryForApi(nextDisplay, pr);
                const iso2 = (chosen || '').toUpperCase();

                flag("CALL_onSearch", { safeQ, iso2 });         //CHECK

                onSearch(safeQ, iso2 ? { countryIso2: iso2 } : undefined);
                inputRef.current?.focus();
                refocusNoScroll();
                setFocused(true);
                return;
            }
            const pts = (pr.cityMatches ?? [])
                .filter(c => Number.isFinite(Number(c.lat)) && Number.isFinite(Number(c.lng)))
                .map(c => ({ lat: Number(c.lat), lng: Number(c.lng) }));

            flag("COMPOSITE_AMBIGUOUS_PINS");                       //CHECK
            // True cross-country ambiguity → show pins and stop
            clearGeo(); // Only clear when showing ambiguity pins
            flag("CALL_onAmbiguous", { pts: pts.length });          //CHECK
           

            onAmbiguous?.({
                outlineIso2: null,
                cities: pts.map(p => ({ ...p })),
                focus: pts.length
                    ? {
                        lat: pts.reduce((s, p) => s + p.lat, 0) / pts.length,
                        lng: pts.reduce((s, p) => s + p.lng, 0) / pts.length,
                    }
                    : undefined,
                label: text,
            });

            setDirty(false);
            setPreviewResult(null);
            inputRef.current?.focus();
            refocusNoScroll();
            setFocused(true);
            return;
        }

        if (pr.kind === ScopeKind.CityInCountry && pr.isAmbiguous) {
            const iso = pr.outlineIso2 ?? (pr.diagnostics && (pr.diagnostics['chosenIso2'] as string | undefined)) ?? null;
            const rawCities = pr.cityMatches ?? [];
            const inCountryRaw = iso
                ? rawCities.filter(c => up(c.countryIso2) === up(iso))
                : rawCities;
            const inCountryUnique = dedupeBy(inCountryRaw, x => x.id ?? `${x.name}|${up(x.countryIso2)}`);

            if (inCountryUnique.length === 1) {
                const chosenCity = inCountryUnique[0];
                const tail = capturedKeywords(pr);
                const phrase = cityPhrase(chosenCity);
                const { text: resolvedText, keywordTail: prunedTail } = applyGeoSuggestion(text, phrase, tail, chosenCity);
                const keywordTail = prunedTail?.trim() ? prunedTail.trim() : undefined;
                const context: GeoPickContext = {
                    fullText: resolvedText.trim(),
                    ...(keywordTail ? { keywordTail } : {}),
                };

                // ✅ checks go HERE
                flag("CITYINCOUNTRY_AMBIGUOUS_UNIQUE_CITY", { iso, count: inCountryUnique.length });        //CHECK
                flag("CALL_onPickCity", { chosenCity: chosenCity.id ?? chosenCity.name ?? null });          //CHECK

                onPickCity?.(chosenCity, context);
                setQ(resolvedText);
                setDirty(false);
                setPreviewResult(null);
                inputRef.current?.focus();
                refocusNoScroll();
                setFocused(true);
                return;
            }

            
            const inCountry = iso
                ? cities.filter(c => (c.countryIso2 ?? '').toUpperCase() === iso.toUpperCase())
                : cities;

            // ✅ checks go HERE
            flag("CITYINCOUNTRY_AMBIGUOUS_PINS", { iso, count: inCountry.length });                 //CHECK

            clearGeo(); // Only clear when showing ambiguity pins

            flag("CALL_onAmbiguous", { outlineIso2: iso ?? null, cities: inCountry.length });       //CHECK

            onAmbiguous?.({
                outlineIso2: iso ?? null,
                cities: inCountry.map(c => ({ lat: Number(c.lat), lng: Number(c.lng), label: c.name ?? undefined })),
                focus: inCountry.length ? {
                    lat: inCountry.reduce((s, p) => s + Number(p.lat), 0) / inCountry.length,
                    lng: inCountry.reduce((s, p) => s + Number(p.lng), 0) / inCountry.length
                } : undefined,
                label: text
            });
            setDirty(false);
            setPreviewResult(null);
            inputRef.current?.focus();
            refocusNoScroll();
            setFocused(true);
            return;
        }

        // --- B) Client-side ambiguity: multiple exact city hits across countries ---
        const EXACT = 0.999;
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
            flag("CLIENT_AMBIGUOUS_PINS", { pins: compositePins.length });      //CHECK
            clearGeo(); // Only clear when showing ambiguity pins
            flag("CALL_onAmbiguous");                                           //CHECK
            onAmbiguous?.({
                outlineIso2: null,
                cities: compositePins,
                focus: {
                    lat: compositePins.reduce((s, p) => s + p.lat, 0) / compositePins.length,
                    lng: compositePins.reduce((s, p) => s + p.lng, 0) / compositePins.length
                },
                label: text
            });
            setDirty(false);
            setPreviewResult(null);
            inputRef.current?.focus();
            refocusNoScroll();
            setFocused(true);
            return;
        }

        // --- C & D) Otherwise proceed with search (keep geo state) ---
        const tail = capturedKeywords(pr);
        const base = bestLabelFromPreview(pr) ?? text;
        const nextText = mergeSuggestion(text, base, tail);
        setQ(nextText);

        // Don't clear geo - let the search results determine new state
        setDirty(false);
        setPreviewResult(null);

        flag("NORMAL_SEARCH", { nextText, iso2: iso2FromPreview(pr) });         //CHECK

        const qForApi = sanitizeQueryForApi(nextText, pr);
        const iso2 = iso2FromPreview(pr);


        flag("CALL_onSearch", { qForApi, iso2 });                               //CHECK

        onSearch(qForApi, iso2 ? { countryIso2: iso2 } : undefined);
        inputRef.current?.focus();
        refocusNoScroll();
        setFocused(true);
    };


    // ----- Build pills (API + synthesized) --------------------------------------

    // From API
    const apiCountryPills = previewResult?.countryMatches ?? [];
    const cityPills = previewResult?.cityMatches ?? [];

    // Display helpers
    const countryNameOf = (c: PreviewGeoCandidate) =>
        normalizeCountryName(c.countryIso2, c.countryName);

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
                    const tail = capturedKeywords(previewResult);
                    const phrase = pick.t === 'city' ? cityPhrase(pick.v) : countryPhrase(pick.v);
                    const { text: textValue, keywordTail: prunedTail } =
                        applyGeoSuggestion(trimmed, phrase, tail, pick.v);
                    const nextTail = prunedTail ?? '';
                    setPendingPick({ ...pick, k: nextTail, text: textValue });
                    if (pick.t === 'city') {
                        onPreviewCity?.(pick.v, prunedTail);
                    } else {
                        onPreviewCountry?.(pick.v, prunedTail);
                    }
                    setDirty(false);
                    setPreviewResult(null);
                    setQ(textValue);
                    refocusNoScroll();
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
                                onClearSearch?.();
                                setQ('');
                                setPendingPick(null);
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

const whitespaceSplitter = /\s+/g;

function labelFor(x: PreviewGeoCandidate, kind?: 'city' | 'country') {
    const iso = (x.countryIso2 ?? x.countryIso3 ?? "").trim();
    const baseName = (x.name ?? "").trim();
    const countryName = (x.countryName ?? "").trim();

    const treatAsCountry =
        kind === 'country' ||
        (kind !== 'city' &&
            baseName &&
            countryName &&
            normalizeFragment(baseName) === normalizeFragment(countryName));

    if (treatAsCountry) {
        const name = normalizeCountryName(x.countryIso2, baseName || countryName);
        return iso ? `${name}, ${up(iso)}` : name;
    }

    if (baseName) {
        return baseName;
    }

    // Fall back to a best-effort city phrase without normalizing
    const phrase = cityPhrase(x);
    if (phrase) return phrase;

    return countryName || iso;
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

    if (topCity?.name) return labelFor(topCity, 'city');

    // Fall back to a country
    const countries = pr.countryMatches ?? [];
    if (countries.length) return labelFor(countries[0], 'country');

    return null;
}

function normalizeFragment(value: string | null | undefined): string {
    return (value ?? '').trim().replace(/\s+/g, ' ').toLowerCase();
}

function splitTokens(value: string | null | undefined): string[] {
    const trimmed = (value ?? '').trim();
    return trimmed ? trimmed.split(/\s+/) : [];
}

function mergeSuggestion(input: string, phrase: string, keywordTail?: string): string {
    const suggestion = phrase.trim();
    const base = (input ?? '').trim();
    const tail = (keywordTail ?? '').trim();

    const baseTokens = splitTokens(base);
    const suggestionTokens = splitTokens(suggestion);
    const tailTokens = tail ? tail.split(whitespaceSplitter).filter(Boolean) : [];

    if (!suggestion) {
        const result = [...baseTokens];
        for (const keyword of tailTokens) {
            const norm = normalizeFragment(keyword);
            if (!result.some(tok => normalizeFragment(tok) === norm)) {
                result.push(keyword);
            }
        }
        return result.join(' ').replace(/\s+/g, ' ').trim();
    }

    let bestStart = -1;
    let bestLen = 0;

    for (let start = 0; start < baseTokens.length; start++) {
        let len = 0;
        while (start + len < baseTokens.length && len < suggestionTokens.length) {
            const baseNorm = normalizeFragment(baseTokens[start + len]);
            const suggestionNorm = normalizeFragment(suggestionTokens[len]);
            if (
                suggestionNorm.startsWith(baseNorm) ||
                baseNorm.startsWith(suggestionNorm)
            ) {
                len++;
            } else {
                break;
            }
        }

        if (len > bestLen) {
            bestLen = len;
            bestStart = start;
        }
    }

    const resultTokens: string[] = [];

    if (bestLen > 0) {
        resultTokens.push(...baseTokens.slice(0, bestStart));
        resultTokens.push(suggestion);
        resultTokens.push(...baseTokens.slice(bestStart + bestLen));
    } else if (baseTokens.length > 0) {
        resultTokens.push(...baseTokens);
        const suggestionNorm = normalizeFragment(suggestion);
        if (!resultTokens.some(tok => normalizeFragment(tok) === suggestionNorm)) {
            resultTokens.push(suggestion);
        }
    } else {
        resultTokens.push(suggestion);
    }

    if (tailTokens.length > 0) {
        for (const keyword of tailTokens) {
            const norm = normalizeFragment(keyword);
            if (!resultTokens.some(tok => normalizeFragment(tok) === norm)) {
                resultTokens.push(keyword);
            }
        }
    }

    return resultTokens.join(' ').replace(/\s+/g, ' ').trim();
}

function capturedKeywords(pr: PreviewResponse | null): string {
    return (pr?.nonGeoKeywords ?? []).filter(Boolean).join(' ').trim();
}

function cityPhrase(candidate: PreviewGeoCandidate): string {
    const name = (candidate.name ?? '').trim();
    const country = normalizeCountryName(candidate.countryIso2, candidate.countryName);
    if (!name) return country || '';
    const nameLower = name.toLowerCase();
    if (country && !nameLower.includes(country.toLowerCase())) {
        return `${name} ${country}`.trim();
    }
    return name;
}


function countryPhrase(candidate: PreviewGeoCandidate): string {
    const display = normalizeCountryName(candidate.countryIso2, candidate.name ?? candidate.countryName);
    if (display) return display;
    const iso = (candidate.countryIso2 ?? candidate.countryIso3 ?? candidate.id ?? '').trim();
    return iso;
}


// --- Smart geo replacement ----------------------------------------------------
function tokensEqualLoose(a: string, b: string): boolean {
    const A = normalizeFragment(a);
    const B = normalizeFragment(b);
    if (!A || !B) return false;
    // exact, or prefix-of-each-other to allow incompletes: "phili" ~ "philippines"
    return A === B || A.startsWith(B) || B.startsWith(A);
}

function isIsoCodeMatch(token: string, iso2?: string | null, iso3?: string | null): boolean {
    const t = normalizeFragment(token);
    if (!t) return false;
    const i2 = normalizeFragment(iso2 ?? '');
    const i3 = normalizeFragment(iso3 ?? '');
    return t === i2 || t === i3;
}

type GeoSuggestionResult = { text: string; keywordTail?: string };

function finalizeWithTail(text: string, keywordTail?: string): string {
    const tailTokens = (keywordTail ?? '').split(whitespaceSplitter).filter(Boolean);
    const resultTokens = splitTokens(text);

    // normalized copies for fuzzy (prefix) dedupe
    const resultNorms = resultTokens.map(normalizeFragment);

    for (const tok of tailTokens) {
        const n = normalizeFragment(tok);
        // Skip if tail token equals OR is a prefix of any existing token,
        // or any existing token is a prefix of the tail token.
        const overlaps = resultNorms.some(rt => rt === n || rt.startsWith(n) || n.startsWith(rt));
        if (overlaps) continue;

        resultTokens.push(tok);
        resultNorms.push(n);
    }

    return resultTokens.join(' ').replace(/\s+/g, ' ').trim();
}

function pruneKeywordTail(
    keywordTail: string | undefined,
    replacedTokens: string[],
    candidate: PreviewGeoCandidate
): string | undefined {
    const tailTokens = (keywordTail ?? '').split(whitespaceSplitter).filter(Boolean);
    if (tailTokens.length === 0) return undefined;

    const replacedNorms = replacedTokens.map(normalizeFragment).filter(Boolean);
    const isoNorms = [normalizeFragment(candidate.countryIso2), normalizeFragment(candidate.countryIso3)].filter(Boolean);
    const countryLabel = normalizeCountryName(candidate.countryIso2, candidate.countryName);
    const countryNorms = splitTokens(countryLabel).map(normalizeFragment).filter(Boolean);
    const shouldDropMacedonia = countryNorms.includes('macedonia');
    const considerIso = replacedTokens.length > 0;

    const filtered = tailTokens.filter(tok => {
        const norm = normalizeFragment(tok);
        if (!norm) return false;

        const matchesReplaced = replacedNorms.some(rt => rt === norm || rt.startsWith(norm) || norm.startsWith(rt));
        if (matchesReplaced) return false;

        if (considerIso) {
            const matchesIso = isoNorms.some(iso => iso === norm || iso.startsWith(norm) || norm.startsWith(iso));
            if (matchesIso) return false;
        }

        if (countryNorms.some(cn => cn === norm || cn.startsWith(norm) || norm.startsWith(cn))) {
            return false;
        }

        if (shouldDropMacedonia && norm === 'macedonia') {
            return false;
        }

        return true;
    });

    if (filtered.length === 0) return undefined;
    return filtered.join(' ');
}

/**
 * Replace the best matching contiguous window of the input with the full geo phrase.
 * Rules:
 *  - If input token count equals geo token count → replace the whole input.
 *  - Else if coverage >= 40% (loose token equality or ISO code equivalence) → replace that window.
 *  - Else fall back to legacy mergeSuggestion behavior.
 */
function applyGeoSuggestion(
    input: string,
    phrase: string,
    keywordTail: string | undefined,
    candidate: PreviewGeoCandidate
): GeoSuggestionResult {
    const baseTokens = splitTokens(input);
    const suggestionTokens = splitTokens(phrase);
    const S = suggestionTokens.length;

    if (S === 0) {
        const prunedTail = pruneKeywordTail(keywordTail, [], candidate);
        const merged = mergeSuggestion(input, phrase, prunedTail);
        return { text: merged, keywordTail: prunedTail };
    }

    const iso2 = candidate.countryIso2 ?? null;
    const iso3 = candidate.countryIso3 ?? null;

    // If the entire query looks like just the geo (same token count), fully replace
    if (baseTokens.length === S) {
        const prunedTail = pruneKeywordTail(keywordTail, baseTokens, candidate);
        return {
            text: finalizeWithTail(phrase.trim(), prunedTail),
            keywordTail: prunedTail,
        };
    }

    // Scan all possible windows of length up to S and measure coverage
    const THRESHOLD = 0.4;
    let bestStart = -1;
    let bestCoverage = 0;
    let bestLen = 0;

    for (let start = 0; start < baseTokens.length; start++) {
        let matchCount = 0;
        let len = 0;
        while (start + len < baseTokens.length && len < S) {
            const bt = baseTokens[start + len];
            const st = suggestionTokens[len];
            if (tokensEqualLoose(bt, st) || isIsoCodeMatch(bt, iso2, iso3)) {
                matchCount++;
            }
            len++;
        }
        const coverage = matchCount / S;
        if (coverage > bestCoverage) {
            bestCoverage = coverage;
            bestStart = start;
            bestLen = Math.min(S, baseTokens.length - start);
        }
    }

    if (bestStart >= 0 && bestCoverage >= THRESHOLD) {
        const replacedTokens = baseTokens.slice(bestStart, bestStart + bestLen);
        const replaced = [
            ...baseTokens.slice(0, bestStart),
            phrase,
            ...baseTokens.slice(bestStart + bestLen)
        ].join(' ');
        const prunedTail = pruneKeywordTail(keywordTail, replacedTokens, candidate);
        return {
            text: finalizeWithTail(replaced, prunedTail),
            keywordTail: prunedTail,
        };
    }

    // Fallback: legacy behavior (keeps partially-typed tails if we couldn't reach 40%)
    const prunedTail = pruneKeywordTail(keywordTail, [], candidate);
    return {
        text: mergeSuggestion(input, phrase, prunedTail),
        keywordTail: prunedTail,
    };
}
function removeWindowOnce(tokens: string[], windowTokens: string[]): string[] {
    if (windowTokens.length === 0) return tokens;
    for (let i = 0; i <= tokens.length - windowTokens.length; i++) {
        let ok = true;
        for (let j = 0; j < windowTokens.length; j++) {
            if (!tokensEqualLoose(tokens[i + j], windowTokens[j])) { ok = false; break; }
        }
        if (ok) {
            return [...tokens.slice(0, i), ...tokens.slice(i + windowTokens.length)];
        }
    }
    return tokens;
}
function primaryCityFromPreview(pr: PreviewResponse | null): PreviewGeoCandidate | null {
    if (!pr) return null;

    const cities = pr.cityMatches ?? [];
    if (cities.length === 0) return null;

    const chosenIso = up(
        (pr.outlineIso2 ?? (pr.diagnostics?.['chosenIso2'] as string | undefined) ?? '') || ''
    );

    if (chosenIso) {
        const match = cities.find(c => up(c.countryIso2 ?? '') === chosenIso);
        if (match) return match;
    }

    return cities[0] ?? null;
}



function cityTokensFromPreview(pr: PreviewResponse | null): string[] {
    if (!pr) return [];

    const chosenIso = up(
        (pr.outlineIso2
            ?? (pr.diagnostics && (pr.diagnostics['chosenIso2'] as string | undefined))
            ?? '') || ''
    );

    const cities = pr.cityMatches ?? [];
    const topCity =
        (chosenIso && cities.find(c => up(c.countryIso2 ?? '') === chosenIso))
        || (cities.length ? cities[0] : null);

    if (!topCity) return [];

    return splitTokens(cityPhrase(topCity));
}
function countryTokensFromPreview(pr: PreviewResponse | null): string[] {
    if (!pr) return [];
    // Prefer the country of the top city; else the top country match
    const city = primaryCityFromPreview(pr);
    const countryIso2 =
        city?.countryIso2
        ?? pr.outlineIso2
        ?? (pr.diagnostics?.['chosenIso2'] as string | undefined)
        ?? pr.countryMatches?.[0]?.countryIso2
        ?? null;


    const countryName =
        normalizeCountryName(countryIso2 ?? undefined,
            city?.countryName
            ?? (pr.countryMatches && pr.countryMatches[0]?.name)
            ?? null);

    return splitTokens(countryName);
}

function sanitizeQueryForApi(displayQ: string, pr: PreviewResponse | null): string {
    // Start from the full display text (e.g., "San Jose Philippines sports")
    let tokens = splitTokens(displayQ);

    // Remove the *country words* (e.g., ["Philippines"])
    const cTokens = countryTokensFromPreview(pr);
    tokens = removeWindowOnce(tokens, cTokens);

    // Also drop raw ISO tokens if the user typed them in q
    const iso2 = (
        pr?.outlineIso2
        ?? (pr?.diagnostics?.['chosenIso2'] as string | undefined)
        ?? pr?.cityMatches?.[0]?.countryIso2
        ?? pr?.countryMatches?.[0]?.countryIso2
        ?? ''
    ).toUpperCase();


    const iso3 = ((pr?.cityMatches && pr.cityMatches[0]?.countryIso3)
        ?? (pr?.countryMatches && pr.countryMatches[0]?.countryIso3)
        ?? '').toUpperCase();

    tokens = tokens.filter(t => !isIsoCodeMatch(t, iso2, iso3));

    const cityTokens = cityTokensFromPreview(pr);
    if (cityTokens.length === 0) {
        return tokens.join(' ');
    }

    const result: string[] = [];
    const seen = new Set<string>();

    for (const ct of cityTokens) {
        const norm = normalizeFragment(ct);
        if (!norm || seen.has(norm)) continue;
        result.push(ct);
        seen.add(norm);
    }

    for (const token of tokens) {
        const norm = normalizeFragment(token);
        if (!norm || seen.has(norm)) continue;
        result.push(token);
        seen.add(norm);
    }

    return result.join(' ');
}

function iso2FromPreview(pr: PreviewResponse | null): string | undefined {
    const iso = (
        pr?.outlineIso2
        ?? (pr?.diagnostics?.['chosenIso2'] as string | undefined)
        ?? pr?.cityMatches?.[0]?.countryIso2
        ?? pr?.countryMatches?.[0]?.countryIso2
        ?? ''
    ).toUpperCase();
    return iso || undefined;
}
function normalizeCountryName(iso2?: string | null, name?: string | null): string {
    const iso = (iso2 ?? '').toUpperCase();
    const raw = (name ?? '').trim();

    // Match old long forms and "North Macedonia" too
    const looksLikeFYROM =
        /former\s+yugoslav/i.test(raw) && /macedonia/i.test(raw);
    const looksLikeNorth =
        /north\s+macedonia/i.test(raw);

    if (iso === 'MK' || looksLikeFYROM || looksLikeNorth) {
        return 'Macedonia';
    }
    return raw;
}
