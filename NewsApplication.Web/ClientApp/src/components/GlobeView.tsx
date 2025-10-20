import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import Globe from 'react-globe.gl';

type GlobeRef = any;

interface Props {
    onPick: (lat: number, lng: number) => void;
    /** Called when a country polygon is clicked (ISO-2 provided). */
    onPickCountry?: (iso2: string, lat: number, lng: number) => void;
    focus?: { lat: number; lng: number; altitude?: number } | null;
    /** ISO-2 of country to outline (e.g. "FR"). Pass null/undefined to clear. */
    highlightIso2?: string | null;
    /** City marker coordinates; pass null/undefined to hide. */
    cityMarker?: { lat: number; lng: number } | null;
}

export default function GlobeView({ onPick, onPickCountry, focus, highlightIso2, cityMarker }: Props) {
    const globeRef = useRef<GlobeRef>(null);
    const wrapRef = useRef<HTMLDivElement>(null);
    const [size, setSize] = useState({ w: 600, h: 600 });

    const [allFeatures, setAllFeatures] = useState<any[] | null>(null);
    const [polyData, setPolyData] = useState<any[]>([]);
    const [points, setPoints] = useState<any[]>([]); // city dot

    /* Measure searchbar height -> CSS var so stage can be 100vh - searchbar */
    useLayoutEffect(() => {
        const sb = document.querySelector<HTMLElement>('.searchbar');
        const setBarH = () => {
            const h = sb?.offsetHeight ?? 0;
            document.documentElement.style.setProperty('--searchbar-h', `${h}px`);
        };
        setBarH();
        window.addEventListener('resize', setBarH);
        return () => window.removeEventListener('resize', setBarH);
    }, []);

    /* Match canvas to wrapper EXACTLY */
    useEffect(() => {
        const el = wrapRef.current;
        if (!el) return;

        const update = () => {
            const r = el.getBoundingClientRect();
            const w = Math.max(320, Math.round(r.width));
            const h = Math.max(240, Math.round(r.height));
            setSize({ w, h });
        };

        const ro = new ResizeObserver(update);
        ro.observe(el);
        update();
        return () => ro.disconnect();
    }, []);

    // Load countries once from /public/data (file can be .json or .geojson)
    useEffect(() => {
        let cancelled = false;
        (async () => {
            try {
                const res = await fetch('/data/countries.json');
                const gj = await res.json();
                if (!cancelled) setAllFeatures(Array.isArray(gj?.features) ? gj.features : []);
            } catch {
                if (!cancelled) setAllFeatures([]);
            }
        })();
        return () => {
            cancelled = true;
        };
    }, []);

    // Helpers to read ISO-2 (handles Natural Earth "-99" quirk)
    const pick = (v: any) => (v == null ? '' : String(v).toUpperCase());
    const getIso = useCallback((p: any): string => {
        const pick = (v: any) => (v == null ? '' : String(v).toUpperCase());
        const p0 = p ?? {};
        const ISO2 = 'ISO2', ISO_A2 = 'ISO_A2', ISO_A2_EH = 'ISO_A2_EH', ADM0_A3 = 'ADM0_A3', adm0_a3 = 'adm0_a3', ISO = 'ISO';

        const isoA2 = pick(p0[ISO_A2]);
        const isoA2eh = pick(p0[ISO_A2_EH]);
        let iso = (!isoA2 || isoA2 === '-99') ? isoA2eh : isoA2;
        if (!iso) iso = pick(p0.iso_a2 ?? p0[ISO2] ?? p0.iso2 ?? p0[ISO] ?? p0[ADM0_A3] ?? p0[adm0_a3]);
        return iso;
    }, []);


    // Filter features by ISO-2 (for the green outline)
    useEffect(() => {
        const isoWanted = highlightIso2?.toUpperCase();
        if (!isoWanted || !allFeatures || allFeatures.length === 0) {
            setPolyData([]);
            return;
        }
        const matches = allFeatures.filter((f: any) => getIso(f?.properties) === isoWanted);
        setPolyData(matches);
    }, [highlightIso2, allFeatures, getIso]);

    // City marker (single point) — RED and smaller (50%)
    useEffect(() => {
        if (cityMarker && Number.isFinite(cityMarker.lat) && Number.isFinite(cityMarker.lng)) {
            setPoints([{ lat: cityMarker.lat, lng: cityMarker.lng }]);
        } else {
            setPoints([]);
        }
    }, [cityMarker]);

    // Limit zoom
    useEffect(() => {
        const g = globeRef.current;
        if (!g) return;
        const controls = g.controls?.();
        if (!controls) return;
        const R = typeof g.getGlobeRadius === 'function' ? g.getGlobeRadius() : 100;
        controls.minDistance = R * 1.5;
        controls.maxDistance = R * 6;
        controls.enableDamping = true;
        controls.dampingFactor = 0.05;
        controls.update();
    }, [size]);

    /* Smooth camera fly when focus changes */
    useEffect(() => {
        if (focus && globeRef.current) {
            globeRef.current.pointOfView(
                { lat: focus.lat, lng: focus.lng, altitude: focus.altitude ?? 1.3 },
                1000
            );
        }
    }, [focus]);

    // --- helpers for polygon centroid (fallback)
    const centroidFromFeature = (f: any): { lat: number; lng: number } => {
        const g = f?.geometry;
        if (!g) return { lat: 0, lng: 0 };

        const ringAvg = (ring: number[][]) => {
            let sumLat = 0, sumLng = 0;
            const n = ring.length || 1;
            for (const [lng, lat] of ring) { sumLat += lat; sumLng += lng; }
            return { lat: sumLat / n, lng: sumLng / n };
        };

        if (g.type === 'Polygon') return ringAvg(g.coordinates[0]);
        if (g.type === 'MultiPolygon') {
            let sumLat = 0, sumLng = 0, k = 0;
            for (const poly of g.coordinates) {
                const avg = ringAvg(poly[0]); sumLat += avg.lat; sumLng += avg.lng; k++;
            }
            return { lat: sumLat / (k || 1), lng: sumLng / (k || 1) };
        }
        return { lat: 0, lng: 0 };
    };

    return (
        <div ref={wrapRef} className="globe-wrap">
            <Globe
                ref={globeRef}
                width={size.w}
                height={size.h}

                /* Single-click anywhere → reverse lookup (keeps city picks working) */
                onGlobeClick={({ lat, lng }: { lat: number; lng: number }) => onPick(lat, lng)}

                /* Clicking a filled country polygon → send ISO-2 to the app so it fetches country articles */
                onPolygonClick={(poly: any, _evt: any, extra?: { lat: number; lng: number }) => {
                    const c = extra && Number.isFinite(extra.lat) && Number.isFinite(extra.lng)
                        ? { lat: extra.lat, lng: extra.lng }
                        : centroidFromFeature(poly);

                    const iso = getIso(poly?.properties);
                    if (iso && typeof onPickCountry === 'function') {
                        onPickCountry(iso, c.lat, c.lng);
                    } else {
                        onPick(c.lat, c.lng); // fallback
                    }
                }}

                rendererConfig={{ alpha: true, antialias: true }}
                backgroundColor="rgba(0,0,0,0)"
                globeImageUrl="//unpkg.com/three-globe/example/img/earth-blue-marble.jpg"
                bumpImageUrl="//unpkg.com/three-globe/example/img/earth-topology.png"
                showAtmosphere
                atmosphereColor="lightskyblue"
                atmosphereAltitude={0.25}

                /* Country outline (stroke only) */
                polygonsData={polyData}
                polygonAltitude={() => 0.01}
                polygonCapColor={() => 'rgba(0,0,0,0)'}
                polygonSideColor={() => 'rgba(0,0,0,0)'}
                polygonStrokeColor={() => '#39FF14'}  /* lime */

                /* City dot — RED and smaller */
                pointsData={points}
                pointLat="lat"
                pointLng="lng"
                pointAltitude={() => 0.02}
                pointColor={() => '#FF3B30'}          /* red */
                pointRadius={0.15}                   /* 50% smaller than 0.35 */
            />
        </div>
    );
}
