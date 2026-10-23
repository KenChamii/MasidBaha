import * as L from 'leaflet';
import { Typhoon } from '../../../core/services/weather.service';
import { WeatherOverlays, WeatherState } from './weather-overlays';

const storm: Typhoon = {
  id: 7,
  name: 'TESTO-26',
  alertLevel: 'Red',
  source: 'JTWC',
  country: 'Philippines',
  reportUrl: 'https://www.gdacs.org/report.aspx?eventid=7',
  lat: 22.7,
  lng: 123.8,
  track: [
    {
      category: 'TS',
      points: [
        [20, 122],
        [22.7, 123.8]
      ]
    },
    {
      category: 'HU',
      points: [
        [22.7, 123.8],
        [24, 124.5]
      ]
    }
  ],
  cone: [
    [
      [22, 123],
      [22, 125],
      [25, 125],
      [25, 123]
    ]
  ],
  windAreas: [
    {
      kmPerHour: 90,
      ring: [
        [21.5, 122.5],
        [21.5, 125.5],
        [24, 125.5],
        [24, 122.5]
      ]
    }
  ],
  forecastPoints: [{ label: '24/10 06:00 UTC', lat: 23, lng: 124 }]
};

const off: WeatherState = {
  radar: { on: false, host: '', frames: [], playing: false },
  satellite: false,
  typhoons: null
};

describe('WeatherOverlays', () => {
  let map: L.Map;
  let overlays: WeatherOverlays;
  let container: HTMLElement;

  const tileLayers = () => {
    const layers: L.TileLayer[] = [];
    map.eachLayer(layer => {
      if (layer instanceof L.TileLayer) layers.push(layer);
    });
    return layers;
  };

  const layerCount = (type: new (...args: never[]) => L.Layer) => {
    let count = 0;
    map.eachLayer(layer => {
      if (layer instanceof type) count++;
    });
    return count;
  };

  beforeEach(() => {
    container = document.createElement('div');
    container.style.width = '400px';
    container.style.height = '300px';
    document.body.appendChild(container);
    map = L.map(container).setView([14.6, 121], 6);
    overlays = new WeatherOverlays(map);
  });

  afterEach(() => {
    vi.useRealTimers();
    overlays.dispose();
    map.remove();
    container.remove();
  });

  it('draws weather in its own pane, so the dark-mode filter on the base tiles cannot touch it', () => {
    expect(map.getPane('weather')).toBeDefined();
    expect(map.getPane('weather')!.style.pointerEvents).toBe('none');
  });

  it('shows the satellite layer only while it is on', () => {
    overlays.sync({ ...off, satellite: true });
    expect(tileLayers()).toHaveLength(1);

    overlays.sync(off);
    expect(tileLayers()).toHaveLength(0);
  });

  it('does not add the satellite layer twice', () => {
    overlays.sync({ ...off, satellite: true });
    overlays.sync({ ...off, satellite: true });

    expect(tileLayers()).toHaveLength(1);
  });

  it('shows the newest radar frame and reports its time', () => {
    const seen: (number | null)[] = [];
    overlays.onRadarFrame = frame => seen.push(frame?.time ?? null);

    overlays.sync({
      ...off,
      radar: {
        on: true,
        host: 'https://tiles.example',
        frames: [
          { time: 100, path: '/a' },
          { time: 200, path: '/b' }
        ],
        playing: false
      }
    });

    expect(seen.at(-1)).toBe(200);
    const visible = tileLayers().filter(layer => layer.options.opacity! > 0);
    expect(visible).toHaveLength(1);
    expect((visible[0] as unknown as { _url: string })._url).toContain('/b/256/');
  });

  it('steps through the frames while playing, and stops when paused', () => {
    vi.useFakeTimers();
    const seen: (number | null)[] = [];
    overlays.onRadarFrame = frame => seen.push(frame?.time ?? null);
    const radar = {
      host: 'https://tiles.example',
      frames: [
        { time: 100, path: '/a' },
        { time: 200, path: '/b' },
        { time: 300, path: '/c' }
      ]
    };

    overlays.sync({ ...off, radar: { ...radar, on: true, playing: true } });
    vi.advanceTimersByTime(1500);
    expect(new Set(seen).size).toBeGreaterThan(1);

    overlays.sync({ ...off, radar: { ...radar, on: true, playing: false } });
    const afterPause = seen.length;
    vi.advanceTimersByTime(3000);
    expect(seen.length).toBe(afterPause + 0);
    expect(seen.at(-1)).toBe(300);
  });

  it('removes the radar when it is switched off', () => {
    overlays.sync({
      ...off,
      radar: { on: true, host: 'https://tiles.example', frames: [{ time: 1, path: '/a' }], playing: false }
    });
    overlays.sync(off);

    expect(tileLayers()).toHaveLength(0);
  });

  it('draws a storm: centre, track, cone, wind area and forecast points', () => {
    overlays.sync({ ...off, typhoons: [storm] });

    expect(layerCount(L.Marker)).toBe(1);
    // Two track segments, the cone and one wind area
    expect(layerCount(L.Polyline)).toBeGreaterThanOrEqual(4);
    expect(layerCount(L.CircleMarker)).toBeGreaterThanOrEqual(1);
  });

  it('removes the storms when the layer is switched off', () => {
    overlays.sync({ ...off, typhoons: [storm] });
    overlays.sync(off);

    expect(layerCount(L.Marker)).toBe(0);
    expect(layerCount(L.Polyline)).toBe(0);
  });

  it('does not redraw storms that have not changed', () => {
    overlays.sync({ ...off, typhoons: [storm] });
    const before = layerCount(L.Polyline);

    overlays.sync({ ...off, typhoons: [storm] });

    expect(layerCount(L.Polyline)).toBe(before);
  });

  it('zooms to a storm', () => {
    overlays.sync({ ...off, typhoons: [storm] });
    const fit = vi.spyOn(map, 'fitBounds');

    overlays.zoomTo(7);
    overlays.zoomTo(999); // unknown: ignored

    expect(fit).toHaveBeenCalledTimes(1);
  });

  it('writes the storm name into the popup as text, never as markup', () => {
    overlays.sync({ ...off, typhoons: [{ ...storm, name: '<img src=x onerror=alert(1)>' }] });

    let popup: HTMLElement | undefined;
    map.eachLayer(layer => {
      if (layer instanceof L.Marker) popup = layer.getPopup()!.getContent() as HTMLElement;
    });

    expect(popup!.querySelector('img')).toBeNull();
    expect(popup!.textContent).toContain('<img src=x onerror=alert(1)>');
    expect(popup!.querySelector('a[href^="https://www.pagasa.dost.gov.ph/"]')).not.toBeNull();
  });
});
