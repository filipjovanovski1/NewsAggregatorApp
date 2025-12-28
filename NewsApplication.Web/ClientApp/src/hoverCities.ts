import type { PreviewResponse } from './api';

export type HoverCity = { lat: number; lng: number; label?: string | null };

/**
 * Returns up to 20 cities (with labels) for a hovered country ISO, using preview data.
 * Filters to matching ISO and finite coordinates, sorts by descending score.
 */
export async function loadTopCitiesForHover(
    iso2: string,
    nameHint: string | null,
    previewFn: (q: string) => Promise<PreviewResponse>
): Promise<HoverCity[]> {
    const query = nameHint && nameHint.trim().length ? nameHint : iso2;
    const res = await previewFn(query);

    const cities = (res.cityMatches ?? [])
        .filter(c => (c.countryIso2 ?? '').toUpperCase() === iso2.toUpperCase())
        .filter(c => Number.isFinite(c.lat) && Number.isFinite(c.lng));

    const sorted = [...cities].sort((a, b) => {
        const as = typeof a.score === 'number' ? a.score : 0;
        const bs = typeof b.score === 'number' ? b.score : 0;
        return bs - as;
    });

    return sorted.slice(0, 20).map(c => ({
        lat: Number(c.lat),
        lng: Number(c.lng),
        label: c.countryName ? `${c.name}, ${c.countryName}` : c.name
    }));
}
