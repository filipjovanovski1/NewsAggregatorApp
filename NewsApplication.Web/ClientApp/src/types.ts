// DTOs based on your NewsApplication_Summary.docx

export type ArticleDto = {
    id: string;
    title: string;
    url: string;
    snippet?: string;
    sourceName?: string;
    imageUrl?: string;
    publishedUtc?: string;
};

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
