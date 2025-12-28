import type { TopCity } from './api';
import { fetchTopCities } from './api';

export type HoverCity = {
    lat: number;
    lng: number;
    label?: string | null;
    id?: string;
    countryIso2?: string | null;
    countryIso3?: string | null;
    name?: string | null;
    population?: number | null;
};


export async function loadTopCitiesForHover(
    iso2: string,
    fetcher: typeof fetchTopCities = fetchTopCities
): Promise<HoverCity[]> {

    const cities: TopCity[] = await fetcher(iso2, 20);

    return cities
        .filter(c => Number.isFinite(c.lat) && Number.isFinite(c.lng))
        .map(c => ({
            lat: Number(c.lat),
            lng: Number(c.lng),
            label: c.countryName ? `${c.name}, ${c.countryName}` : c.name,
            id: c.id,
            countryIso2: c.countryIso2,
            countryIso3: c.countryIso3,
            name: c.name,
            population: c.population ?? null
        }));
}
