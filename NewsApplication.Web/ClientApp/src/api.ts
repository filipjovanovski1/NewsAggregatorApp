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
  uiPage: number;   // which UI page was requested
  pageSize: number; // always 6
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
  countryIso2?: string;
  countryIso3?: string;
  cityId?: string;
  focusLat?: number;
  focusLng?: number;
};

// The request body we send to /scope/resolve
export type ResolveScopeBody =
  | { q: string }
    | { city: { id: string; name: string; countryIso2: string }; q?: string }
    | { country: { iso2: string; iso3?: string; name?: string }; q?: string };

export type ReverseScopeBody = { lat: number; lng: number };

export const ScopeKind = {
  None: 0,
  City: 1,
  Country: 2,
  CityInCountry: 3,
  Other: 4,
  Composite: 5
} as const;

export type ScopeKind = (typeof ScopeKind)[keyof typeof ScopeKind];

export type PreviewGeoCandidate = {
  id: string;
  name: string;
  countryName?: string | null;
  countryIso2?: string | null;
  countryIso3?: string | null;
  lat?: number | null;
  lng?: number | null;
  score: number;
};
export type PreviewToken = {
  raw: string;
  normalized: string;
  matchedEntityType: string;
  countries?: PreviewGeoCandidate[];
  cities?: PreviewGeoCandidate[];
};

export type PreviewResponse = {
  originalQuery: string;
  kind: ScopeKind;
    isAmbiguous: boolean;
    outlineIso2?: string | null;
  canSearch: boolean;
  countryMatches: PreviewGeoCandidate[];
  cityMatches: PreviewGeoCandidate[];
  citiesGroupedByCountry: Record<string, PreviewGeoCandidate[]>;
  nonGeoKeywords: string[];
  tokens?: PreviewToken[];
  targets: PreviewGeoCandidate[];
  diagnostics?: Record<string, unknown> | null;
};

// ---------- tiny typed fetch helpers ----------
async function getJSON<T>(input: RequestInfo | URL, init?: RequestInit): Promise<T> {
  const r = await fetch(input, init);
  if (!r.ok) {
    const msg = await r.text().catch(() => String(r.status));
    throw new Error(`HTTP ${r.status}: ${msg}`);
  }
  return (await r.json()) as T;
}

function isAbortError(err: unknown): boolean {
    // Browser: DOMException with name 'AbortError'
    if (err instanceof DOMException && err.name === 'AbortError') return true;
    // Some environments stringify differently
    if (typeof err === 'object' && err !== null) {
        const name = (err as { name?: unknown }).name;
        const msg = (err as { message?: unknown }).message;
        if (name === 'AbortError') return true;
        if (typeof msg === 'string' && msg.toLowerCase().includes('aborted')) return true;
    }
    return false;
}

export function makeAbortableGetJSON<T>() {
    let last: AbortController | null = null;

    return async (input: RequestInfo | URL, init?: RequestInit): Promise<T> => {
        if (last) last.abort();
        const ac = new AbortController();
        last = ac;

        try {
            const r = await fetch(input, { ...init, signal: ac.signal });
            if (!r.ok) {
                const msg = await r.text().catch(() => String(r.status));
                throw new Error(`HTTP ${r.status}: ${msg}`);
            }
            return (await r.json()) as T;
        } catch (err: unknown) {
            if (isAbortError(err)) {
                // Let callers choose to ignore aborts — keep the Promise rejected
                throw err; // no `any`, no `Promise.reject`, stays type-safe
            }
            // Normalize non-Error throwables
            throw err instanceof Error ? err : new Error(String(err));
        } finally {
            if (last === ac) last = null; // tidy up the controller reference
        }
    };
}

const getJSONAbortable = makeAbortableGetJSON<unknown>() as <T>(i: RequestInfo | URL, init?: RequestInit) => Promise<T>;

// ---------- API calls ----------
export function preview(q: string): Promise<PreviewResponse> {
  // Use abortable fetch for typeahead UX
  return getJSONAbortable<PreviewResponse>(`/search/preview?q=${encodeURIComponent(q)}`);
}

export function resolveScope(body: ResolveScopeBody): Promise<ResolveScopeResponse> {
  return getJSON<ResolveScopeResponse>('/scope/resolve', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

export function reverseScope(body: ReverseScopeBody): Promise<ResolveScopeResponse> {
  return getJSON<ResolveScopeResponse>('/scope/reverse', {
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
