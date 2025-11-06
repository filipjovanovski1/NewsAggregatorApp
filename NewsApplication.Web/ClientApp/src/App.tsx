import { useCallback, useEffect, useState } from 'react';
import SearchBar from './components/SearchBar';
import GlobeView from './components/GlobeView';
import ArticleOverlay from './components/ArticleOverlay';
import type { ArticleDto } from './types';
import { resolveScope, searchArticles, prewarm, toArticleDto } from './api';

const UI_PAGE_SIZE = 6;

export default function App() {
    const [scopeKey, setScopeKey] = useState<string | null>(null);
    const [label, setLabel] = useState<string>('');   // shown in SearchBar + overlay title
    const [page, setPage] = useState(1);

    const [items, setItems] = useState<ArticleDto[]>([]);
    const [total, setTotal] = useState<number | undefined>(undefined);
    const [canPrev, setCanPrev] = useState(false);
    const [canNext, setCanNext] = useState(false);
    const [nextProviderPage, setNextProviderPage] = useState<number | null>(null);

    // Load current UI page (6 items)
    const load = useCallback(async (uiPage: number) => {
        if (!scopeKey) return;
        const res = await searchArticles(scopeKey, uiPage);
        setItems(res.items.map(toArticleDto));
        setPage(res.uiPage);
        setCanPrev(res.hasNewer);
        setCanNext(res.hasOlder);
        setTotal(res.totalDistinct);
        setNextProviderPage(res.prefetch?.providerPage ?? null);
    }, [scopeKey]);

    // Keep one provider page ahead warm (background)
    useEffect(() => {
        if (scopeKey && nextProviderPage != null) {
            prewarm(scopeKey, nextProviderPage).catch(() => { });
        }
    }, [scopeKey, nextProviderPage]);

    // Resolve (q/country/city) → set key/label → load page 1
    async function resolveAndLoad(body: Parameters<typeof resolveScope>[0]) {
        const res = await resolveScope(body);
        setScopeKey(res.scopeKey);
        setLabel(res.label);
        await load(1); // server preloads provider pages 1–2 for a brand-new scope
    }

    // SearchBar submit
    async function onSearch(q: string) {
        if (!q.trim()) return;
        await resolveAndLoad({ q });
    }

    // Globe clicks
    async function onPick(lat: number, lng: number) {
        // If you add reverse-geo → city later, switch to { city: {...} }.
        await resolveAndLoad({ q: `${lat}, ${lng}` });
    }

    // Country polygon click (now passes iso2 + iso3)
    async function onPickCountry(iso2: string, iso3: string | null, _lat: number, _lng: number) {
        await resolveAndLoad({ country: { iso2, iso3: iso3 ?? undefined, name: iso2 } });
    }

    return (
        <div className="app">
            <div className="topbar">
                <SearchBar inline value={label} onSearch={onSearch} />
            </div>

            <div className="stage">
                <div className="globe-wrap">
                    <GlobeView onPick={onPick} onPickCountry={onPickCountry} />
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
                    onPrev={() => load(page - 1)}
                    onNext={() => load(page + 1)}
                    onClose={() => { setScopeKey(null); setItems([]); }}
                />
            )}
        </div>
    );
}
