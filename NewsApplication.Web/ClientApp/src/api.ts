// src/api.ts

// ---------- Shared client-side DTOs you actually render ----------
export type ArticleDto = {
    id: string;
    title: string;
    url: string;
    snippet?: string;
    sourceName?: string;
    imageUrl?: string;
    publishedUtc?: string; // ISO string
};

// Server-side item (what /articles/search returns per item)
export type SearchItem = {
    articleId: string;
    provider: string;
    title: string;
    description?: string;
    imageUrl?: string;
    publisher?: string;
    url: string;
    publishedTime: string;   // ISO string
    categories?: string[];
};

// Entire /articles/search response (server)
export type SearchResponse = {
    scopeKey: string;
    uiPage: number;                          // which UI page was requested
    pageSize: number;                        // always 6
    hasNewer: boolean;
    hasOlder: boolean;
    totalDistinct: number;
    nextUiPage: number;
    prefetch?: { providerPage: number; providerPageSize: number };
    items: SearchItem[];
};

// /scope/resolve
export type ResolveScopeResponse = {
    scopeKey: string;
    kind: 'city' | 'country' | 'query';
    label: string;
};

// The request body we send to /scope/resolve
export type ResolveScopeBody =
    | { q: string }
    | { city: { id: number; name: string; countryIso2: string } }
    | { country: { iso2: string; name: string } };

// /search/preview — only the bits we need for pills
export type PreviewCountryMatch = {
    Iso2?: string;
    CountryIso2?: string;
    Display?: string;
    Name?: string;
};
export type PreviewCityMatch = {
    Id?: number;
    CityId?: number;
    CountryIso2?: string;
    Display?: string;
    Name?: string;
};
export type PreviewResponse = {
    CountryMatches?: PreviewCountryMatch[];
    CityMatches?: PreviewCityMatch[];
};

// ---------- tiny typed fetch helper ----------
async function getJSON<T>(input: RequestInfo | URL, init?: RequestInit): Promise<T> {
    const r = await fetch(input, init);
    if (!r.ok) {
        const msg = await r.text().catch(() => String(r.status));
        throw new Error(`HTTP ${r.status}: ${msg}`);
    }
    return (await r.json()) as T;
}

// ---------- API calls ----------
export function preview(q: string): Promise<PreviewResponse> {
    return getJSON<PreviewResponse>(`/search/preview?q=${encodeURIComponent(q)}`);
}

export function resolveScope(body: ResolveScopeBody): Promise<ResolveScopeResponse> {
    return getJSON<ResolveScopeResponse>('/scope/resolve', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
    });
}

export function searchArticles(scopeKey: string, uiPage: number): Promise<SearchResponse> {
    return getJSON<SearchResponse>(`/articles/search?scopeKey=${encodeURIComponent(scopeKey)}&uiPage=${uiPage}`, {
        method: 'POST',
    });
}

export function prewarm(scopeKey: string, providerPage: number): Promise<Record<string, unknown>> {
    return getJSON<Record<string, unknown>>(
        `/articles/cache/fetch?scopeKey=${encodeURIComponent(scopeKey)}&page=${providerPage}`,
        { method: 'POST' }
    );
}

// ---------- Mapping helper: SearchItem -> ArticleDto for your UI ----------
export function toArticleDto(x: SearchItem): ArticleDto {
    return {
        id: x.articleId,
        title: x.title,
        url: x.url,
        snippet: x.description,
        sourceName: x.publisher ?? x.provider,
        imageUrl: x.imageUrl,
        publishedUtc: x.publishedTime
    };
}
