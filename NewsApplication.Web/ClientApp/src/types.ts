// DTOs based on your NewsApplication_Summary.docx
export interface ArticleDto {
    id: string;
    title: string;
    url: string;
    publishedUtc: string; // ISO string
    sourceName: string;
    snippet?: string | null;
    countryIso2?: string | null;
    cityName?: string | null;
    imageUrl?: string | null; // optional if you have it
    category?: string | null;
}

export type ApiPlace = {
    kind: 'country' | 'city';
    idOrIso: string | number;        // can be "FR" or 1001, etc.
    name: string;
    lat?: number;
    lng?: number;
    altitude?: number;
    countryIso2?: string;            // present for cities if backend sends it
};

export interface CountryDto {
    iso2: string;
    name: string;
}

export interface CityDto {
    id: string | number;
    name: string;
    countryIso2: string;
    lat?: number;
    lng?: number;
}

export type PagedResult<T> = {
    items: T[];
    total: number;
    page: number;
    pageSize: number;
}

export type LocationSelection =
    | { kind: 'country'; iso2: string; name?: string; lat?: number; lng?: number }
    | { kind: 'city'; id: string | number; name?: string; countryIso2?: string; lat?: number; lng?: number;  };
