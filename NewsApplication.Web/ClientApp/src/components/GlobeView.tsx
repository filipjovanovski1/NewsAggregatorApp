import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import type React from 'react';
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

type Point = {
    lat: number;
    lng: number;
    label?: string | null;
    id?: string | null;
    countryIso2?: string | null;
    countryIso3?: string | null;
    name?: string | null;
    population?: number | null;
};

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
    onPickCountry?: (iso2: string, iso3: string | null, name: string | null, lat: number, lng: number) => void;
    focus?: { lat: number; lng: number; altitude?: number } | null;
    highlightIso2?: string | null;
    cityMarker?: Point | null;
    cityMarkers?: Point[] | null;
    onLabelClick?: (point: Point) => void;
    onCountryHover?: (iso2: string | null) => void;
    onPointHover?: (isHovering: boolean) => void;
}

export default function GlobeView({
    onPick,
    onPickCountry,
    focus,
    highlightIso2,
    cityMarker,
    cityMarkers,
    onLabelClick,
    onCountryHover,
    onPointHover,
}: Props) {
    const globeRef = useRef<GlobeApi | null>(null);
    const wrapRef = useRef<HTMLDivElement>(null);

    const [size, setSize] = useState({ w: 600, h: 600 });
    const [allFeatures, setAllFeatures] = useState<Feature[] | null>(null);
    const [polyData, setPolyData] = useState<Feature[]>([]);
    const [points, setPoints] = useState<Point[]>([]);
    const [hoverLabel, setHoverLabel] = useState<Point | null>(null);

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

    // Load countries once from /public/data
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

    // Uppercase string value of a property
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

    const firstUp = useCallback((props: Record<string, unknown>, keys: string[]): string => {
        for (const k of keys) {
            const v = upOf(props, k);
            if (v) return v;
        }
        return '';
    }, []);

    const getIso = useCallback((props: Record<string, unknown> | undefined): string => {
        if (!props) return '';

        const ISO_A2 = upOf(props, 'ISO_A2');
        const ISO_A2_EH = upOf(props, 'ISO_A2_EH');

        let iso = (!ISO_A2 || ISO_A2 === '-99') ? ISO_A2_EH : ISO_A2;

        if (!iso) {
            iso = firstUp(props, [
                'iso_a2',
                'ISO2',
                'iso2',
                'ISO',
                'ADM0_A3',
                'adm0_a3'
            ]);
        }
        return iso;
    }, [firstUp]);

    const getIso3 = useCallback((props: Record<string, unknown> | undefined): string | null => {
        if (!props) return null;

        const a3 = firstUp(props, [
            'ISO_A3',
            'iso_a3',
            'ADM0_A3',
            'adm0_a3'
        ]);

        return a3 || null;
    }, [firstUp]);

    // Set polygon data to only the highlighted country
    useEffect(() => {
        const isoWanted = highlightIso2?.toUpperCase();
        if (!isoWanted || !allFeatures || allFeatures.length === 0) {
            setPolyData([]);
            return;
        }
        const matches = allFeatures.filter(f => getIso(f.properties) === isoWanted);
        setPolyData(matches);
    }, [highlightIso2, allFeatures, getIso]);

    // Process city markers - prevent unnecessary updates by checking if data actually changed
    useEffect(() => {
        const normalize = (m?: Point | null): Point | null =>
            m && Number.isFinite(m.lat) && Number.isFinite(m.lng)
                ? {
                    lat: Number(m.lat),
                    lng: Number(m.lng),
                    label: m.label ?? null,
                    id: m.id ?? null,
                    countryIso2: m.countryIso2 ?? null,
                    countryIso3: m.countryIso3 ?? null,
                    name: m.name ?? null,
                    population: m.population ?? null
                }
                : null;

        // If we have an array of markers, use those (hover cities)
        if (Array.isArray(cityMarkers) && cityMarkers.length > 0) {
            const good = cityMarkers.map(normalize).filter(Boolean) as Point[];

            setPoints(good);

            return;
        }

        // Single marker case (selected city)
        const single = normalize(cityMarker);
        if (single) {
            setPoints(prev => {
                if (prev.length === 1 && prev[0].id === single.id) {
                    return prev; // Same marker, don't update
                }
                return [single];
            });
            // For single marker, show its label immediately
            setHoverLabel(single);
        } else {
            setPoints(prev => prev.length === 0 ? prev : []);
            setHoverLabel(null);
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

    // Determine if we should show hover labels (only for arrays of markers)
    const shouldShowHoverLabels = Array.isArray(cityMarkers) && cityMarkers.length > 1;

    return (
        <div ref={wrapRef} className="globe-wrap">
            <Globe
                ref={globeRef as unknown as React.RefObject<GlobeApi>}
                width={size.w}
                height={size.h}
                pointLabel={() => ''}
                // Clicking anywhere NOT on the highlighted polygon
                onGlobeClick={({ lat, lng }: { lat: number; lng: number }) => onPick(lat, lng)}
                // Clicking the HIGHLIGHTED polygon
                onPolygonClick={(poly: Feature, _evt: unknown, extra?: { lat: number; lng: number }) => {
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
                        onPickCountry(iso2, iso3, name, c.lat, c.lng);
                    } else {
                        onPick(c.lat, c.lng);
                    }
                }}
                onPolygonHover={(poly?: Feature) => {
                    if (onCountryHover) {
                        const iso2 = poly ? getIso(poly?.properties) : null;
                        onCountryHover(iso2 || null);
                    }
                }}
                rendererConfig={{ alpha: true, antialias: true }}
                backgroundColor="rgba(0,0,0,0)"
                globeImageUrl="//unpkg.com/three-globe/example/img/earth-blue-marble.jpg"
                bumpImageUrl="//unpkg.com/three-globe/example/img/earth-topology.png"
                showAtmosphere
                atmosphereColor="lightskyblue"
                atmosphereAltitude={0.25}
                // Country outline (only the highlighted country)
                polygonsData={polyData}
                polygonAltitude={() => 0.01}
                polygonCapColor={() => 'rgba(0,0,0,0)'}
                polygonSideColor={() => 'rgba(0,0,0,0)'}
                polygonStrokeColor={() => '#39FF14'}
                // City dots
                pointsData={points}
                pointLat="lat"
                pointLng="lng"
                pointAltitude={() => 0.02}
                pointColor={() => '#FF3B30'}
                pointRadius={0.15}
                onPointClick={(p: unknown) => {
                    if (onLabelClick) {
                        onLabelClick(p as Point);
                    }
                }}
                onPointHover={(p: unknown) => {
                    if (wrapRef.current) {
                        wrapRef.current.style.cursor = p ? 'pointer' : '';
                    }

                    // Notify parent about point hover state
                    if (onPointHover) {
                        onPointHover(!!p);
                    }

                    // Update hover label if we're showing multiple markers
                    if (shouldShowHoverLabels) {
                        setHoverLabel(p ? (p as Point) : null);
                    }
                }}
               
                // HTML labels (only show the hovered point's label)
                htmlElementsData={hoverLabel ? [hoverLabel] : []}
                htmlLat="lat"
                htmlLng="lng"
                htmlElement={(p: unknown) => {
                    const point = p as Point;

                    const cityOnly = (() => {
                        const raw = (point.name ?? point.label ?? '').trim();
                        if (!raw) return '';
                        return raw.split(',')[0].trim(); // "Saint Petersburg, Russian Federation" -> "Saint Petersburg"
                    })();

                    const el = document.createElement('div');
                    el.className = 'globe-html-label';

                    const span = document.createElement('span');
                    span.textContent = cityOnly;
                    el.appendChild(span);

                    el.onmouseenter = () => onPointHover?.(true);
                    el.onmouseleave = () => onPointHover?.(false);

                    el.onclick = (evt) => {
                        evt.stopPropagation();
                        onLabelClick?.(point);
                    };

                    return el;
                }}

            />
        </div>
    );
}