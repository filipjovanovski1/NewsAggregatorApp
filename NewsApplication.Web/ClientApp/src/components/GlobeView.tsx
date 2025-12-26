import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import Globe from 'react-globe.gl';

/* ---------- Minimal types we actually use ---------- */
type LngLat = [number, number];

interface PolygonGeometry {
    type: 'Polygon';
    coordinates: LngLat[][];
}
interface MultiPolygonGeometry {
    type: 'MultiPolygon';
    coordinates: LngLat[][][];
}
type Geometry = PolygonGeometry | MultiPolygonGeometry | null;

interface Feature {
    type: 'Feature';
    properties: Record<string, unknown>;
    geometry: Geometry;
}

type Point = { lat: number; lng: number };

type GlobeControls = {
    minDistance: number;
    maxDistance: number;
    enableDamping: boolean;
    dampingFactor: number;
    update: () => void;
};

type GlobeApi = {
    controls?: () => GlobeControls | undefined;
    getGlobeRadius?: () => number;
    pointOfView?: (pov: { lat: number; lng: number; altitude: number }, ms?: number) => void;
};

interface Props {
    onPick: (lat: number, lng: number) => void;
    /** Called when a country polygon is clicked (ISO-2 provided). */
    onPickCountry?: (iso2: string, iso3: string | null, name: string | null, lat: number, lng: number) => void;
    focus?: { lat: number; lng: number; altitude?: number } | null;
    /** ISO-2 of country to outline (e.g. "FR"). Pass null/undefined to clear. */
    highlightIso2?: string | null;
    /** City marker coordinates; pass null/undefined to hide. */
    cityMarker?: { lat: number; lng: number } | null;
    /** Array of city markers (takes precedence over cityMarker when provided). */
    cityMarkers?: { lat: number; lng: number }[] | null;
}

export default function GlobeView({
    onPick,
    onPickCountry,
    focus,
    highlightIso2,
    cityMarker,
    cityMarkers,
}: Props) {
    const globeRef = useRef<GlobeApi | null>(null);
    const wrapRef = useRef<HTMLDivElement>(null);

    const [size, setSize] = useState({ w: 600, h: 600 });
    const [allFeatures, setAllFeatures] = useState<Feature[] | null>(null);
    const [polyData, setPolyData] = useState<Feature[]>([]);
    const [points, setPoints] = useState<Point[]>([]);

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
                const features = Array.isArray(gj?.features) ? (gj.features as Feature[]) : [];
                if (!cancelled) setAllFeatures(features);
            } catch {
                if (!cancelled) setAllFeatures([]);
            }
        })();
        return () => {
            cancelled = true;
        };
    }, []);

    // Uppercase string value of a property, tolerant of unknowns
    function upOf(props: Record<string, unknown>, key: string): string {
        const v = props[key];
        if (v == null) return '';
        return typeof v === 'string' ? v.toUpperCase() : String(v).toUpperCase();
    }

    function strOf(props: Record<string, unknown> | undefined, key: string): string {
        if (!props) return '';
        const v = props[key];
        if (v == null) return '';
        return typeof v === 'string' ? v : String(v);
    }

    function firstString(props: Record<string, unknown> | undefined, keys: string[]): string {
        if (!props) return '';
        for (const k of keys) {
            const raw = strOf(props, k).trim();
            if (raw) return raw;
        }
        return '';
    }

    // Return the first non-empty uppercased value among keys
    function firstUp(props: Record<string, unknown>, keys: string[]): string {
        for (const k of keys) {
            const v = upOf(props, k);
            if (v) return v;
        }
        return '';
    }

    // Extract ISO-2 (handles Natural Earth "-99" quirk)
    const getIso = useCallback((props: Record<string, unknown> | undefined): string => {
        if (!props) return '';

        const ISO_A2 = upOf(props, 'ISO_A2');
        const ISO_A2_EH = upOf(props, 'ISO_A2_EH');

        // Prefer true A2; NE sometimes uses -99 and stores usable code in ISO_A2_EH
        let iso = (!ISO_A2 || ISO_A2 === '-99') ? ISO_A2_EH : ISO_A2;

        // Fallbacks (some datasets put codes in alternative fields)
        if (!iso) {
            iso = firstUp(props, [
                'iso_a2',     // lower-case variant
                'ISO2',       // generic alt
                'iso2',
                'ISO',        // generic alt (be cautious)
                'ADM0_A3',    // last-resort: often ISO-3; only as a fallback to avoid empty
                'adm0_a3'
            ]);
        }
        return iso;
    }, []);

    const getIso3 = useCallback((props: Record<string, unknown> | undefined): string | null => {
        if (!props) return null;

        const a3 = firstUp(props, [
            'ISO_A3',      // standard
            'iso_a3',      // lower-case variant
            'ADM0_A3',     // Natural Earth commonly has this
            'adm0_a3'
        ]);

        return a3 || null;
    }, []);

    // IMPORTANT: polyData only contains the highlighted country's features.
    // This means onPolygonClick ONLY fires when clicking the highlighted country.
    // Clicking anywhere else (including other countries) triggers onGlobeClick → onPick → reverse lookup
    useEffect(() => {
        const isoWanted = highlightIso2?.toUpperCase();
        if (!isoWanted || !allFeatures || allFeatures.length === 0) {
            setPolyData([]);
            return;
        }
        const matches = allFeatures.filter(f => getIso(f.properties) === isoWanted);
        setPolyData(matches);
    }, [highlightIso2, allFeatures, getIso]);

    // City markers: prefer array; fallback to single marker
    useEffect(() => {
        if (Array.isArray(cityMarkers) && cityMarkers.length > 0) {
            const good = cityMarkers.filter(
                (m): m is { lat: number; lng: number } =>
                    Number.isFinite(m?.lat) && Number.isFinite(m?.lng)
            );
            setPoints(good);
            return;
        }

        if (cityMarker && Number.isFinite(cityMarker.lat) && Number.isFinite(cityMarker.lng)) {
            setPoints([{ lat: cityMarker.lat, lng: cityMarker.lng }]);
        } else {
            setPoints([]);
        }
    }, [cityMarkers, cityMarker]);

    // Limit zoom / damping
    useEffect(() => {
        const g = globeRef.current;
        const ctrls = g?.controls?.();
        if (!g || !ctrls) return;

        const R = g.getGlobeRadius?.() ?? 100;
        ctrls.minDistance = R * 1.5;
        ctrls.maxDistance = R * 6;
        ctrls.enableDamping = true;
        ctrls.dampingFactor = 0.05;
        ctrls.update();
    }, [size]);

    /* Smooth camera fly when focus changes */
    useEffect(() => {
        if (focus && globeRef.current?.pointOfView) {
            globeRef.current.pointOfView(
                { lat: focus.lat, lng: focus.lng, altitude: focus.altitude ?? 1.3 },
                1000
            );
        }
    }, [focus]);

    // Compute a rough centroid if click coords aren't provided
    const centroidFromFeature = (f: Feature): { lat: number; lng: number } => {
        const g = f.geometry;
        if (!g) return { lat: 0, lng: 0 };

        const ringAvg = (ring: LngLat[]) => {
            let sumLat = 0;
            let sumLng = 0;
            const n = ring.length || 1;
            for (const [lng, lat] of ring) {
                sumLat += lat;
                sumLng += lng;
            }
            return { lat: sumLat / n, lng: sumLng / n };
        };

        if (g.type === 'Polygon') {
            return ringAvg(g.coordinates[0] ?? []);
        }
        if (g.type === 'MultiPolygon') {
            let sumLat = 0, sumLng = 0, k = 0;
            for (const poly of g.coordinates) {
                const avg = ringAvg(poly[0] ?? []);
                sumLat += avg.lat; sumLng += avg.lng; k++;
            }
            return { lat: sumLat / (k || 1), lng: sumLng / (k || 1) };
        }
        return { lat: 0, lng: 0 };
    };

    return (
        <div ref={wrapRef} className="globe-wrap">
            {/* 
                CLICK BEHAVIOR:
                
                1. Clicking the HIGHLIGHTED POLYGON (green outline):
                   → onPolygonClick fires → calls onPickCountry
                   
                2. Clicking ANYWHERE ELSE (ocean, other countries):
                   → onGlobeClick fires → calls onPick → reverse lookup
                   
                The highlighted country does NOT constrain other clicks.
            */}
            <Globe
                ref={globeRef as unknown as React.MutableRefObject<GlobeApi>}
                width={size.w}
                height={size.h}
                // Clicking anywhere NOT on the highlighted polygon → reverse lookup
                onGlobeClick={({ lat, lng }: { lat: number; lng: number }) => onPick(lat, lng)}
                // Clicking the HIGHLIGHTED polygon → search for that country
                onPolygonClick={(poly: Feature, _evt: unknown, extra?: { lat: number; lng: number }) => {
                    // Extract country info from the CLICKED polygon (not from highlightIso2 state)
                    const c = extra && Number.isFinite(extra.lat) && Number.isFinite(extra.lng)
                        ? { lat: extra.lat, lng: extra.lng }
                        : centroidFromFeature(poly);

                    const iso2 = getIso(poly?.properties);
                    const iso3 = getIso3(poly?.properties);
                    const name = firstString(poly?.properties, [
                        'NAME_EN',
                        'NAME',
                        'ADMIN',
                        'SOVEREIGNT',
                        'ADMIN_NAME'
                    ]) || null;

                    if (iso2 && typeof onPickCountry === 'function') {
                        // Search for the country that was clicked (from poly.properties)
                        onPickCountry(iso2, iso3, name, c.lat, c.lng);
                    } else {
                        // Fallback to reverse lookup if no ISO2 found
                        onPick(c.lat, c.lng);
                    }
                }}

                rendererConfig={{ alpha: true, antialias: true }}
                backgroundColor="rgba(0,0,0,0)"
                globeImageUrl="//unpkg.com/three-globe/example/img/earth-blue-marble.jpg"
                bumpImageUrl="//unpkg.com/three-globe/example/img/earth-topology.png"
                showAtmosphere
                atmosphereColor="lightskyblue"
                atmosphereAltitude={0.25}
                // Country outline (stroke only) - ONLY the highlighted country
                polygonsData={polyData}
                polygonAltitude={() => 0.01}
                polygonCapColor={() => 'rgba(0,0,0,0)'}
                polygonSideColor={() => 'rgba(0,0,0,0)'}
                polygonStrokeColor={() => '#39FF14'}
                // City dot(s)
                pointsData={points}
                pointLat="lat"
                pointLng="lng"
                pointAltitude={() => 0.02}
                pointColor={() => '#FF3B30'}
                pointRadius={0.15}
            />
        </div>
    );
}