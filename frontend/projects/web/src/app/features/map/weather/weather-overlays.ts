import * as L from 'leaflet';
import { NASA_RAIN_TILES, PAGASA_LINKS, RadarFrame, Typhoon } from '../../../core/services/weather.service';

// What the map should be showing right now (the store decides; this class makes it so)
export interface WeatherState {
  radar: { on: boolean; host: string; frames: RadarFrame[]; playing: boolean };
  satellite: boolean;
  // null = typhoons are off
  typhoons: Typhoon[] | null;
}

export const ALERT_COLOR: Record<string, string> = { Red: '#d62839', Orange: '#ff7a47', Green: '#1b9e77' };
export const CATEGORY_COLOR: Record<string, string> = { TD: '#4aa3ff', TS: '#ffb703', HU: '#e63946' };
export const WIND_COLOR: Record<number, string> = { 60: '#ffd166', 90: '#ff7a47', 120: '#e63946' };

const RADAR_OPACITY = 0.65;
const FRAME_MS = 700;

// Draws weather on top of the base map. Radar and satellite go in their own pane, because the dark-mode
// filter on the base tiles would otherwise invert their colours.
export class WeatherOverlays {
  private readonly radarLayers = new Map<number, L.TileLayer>();
  private radarKey = '';
  private radarIndex = 0;
  private playTimer?: ReturnType<typeof setInterval>;
  private satellite?: L.TileLayer;
  private readonly storms = L.layerGroup();
  private readonly stormBounds = new Map<number, L.LatLngBounds>();
  private stormsKey = '';

  // Reports which frame is showing, so the panel can print its time
  onRadarFrame: (frame: RadarFrame | null) => void = () => undefined;

  constructor(private readonly map: L.Map) {
    const pane = map.createPane('weather');
    pane.style.zIndex = '250'; // above the base tiles (200), below shapes and markers
    pane.style.pointerEvents = 'none';
  }

  sync(state: WeatherState): void {
    this.syncRadar(state.radar);
    this.syncSatellite(state.satellite);
    this.syncTyphoons(state.typhoons);
  }

  zoomTo(stormId: number): void {
    const bounds = this.stormBounds.get(stormId);
    if (bounds?.isValid()) this.map.fitBounds(bounds, { padding: [40, 40], maxZoom: 8 });
  }

  dispose(): void {
    this.stopPlaying();
    this.radarLayers.forEach(layer => layer.remove());
    this.radarLayers.clear();
    this.satellite?.remove();
    this.storms.remove();
  }

  // ---- rain radar ----

  private syncRadar(radar: WeatherState['radar']): void {
    if (!radar.on || radar.frames.length === 0) {
      this.stopPlaying();
      this.radarLayers.forEach(layer => layer.remove());
      this.radarLayers.clear();
      this.radarKey = '';
      this.onRadarFrame(null);
      return;
    }

    // New frames (a refresh) replace the old layers
    const key = radar.host + radar.frames.map(f => f.path).join();
    if (key !== this.radarKey) {
      this.radarLayers.forEach(layer => layer.remove());
      this.radarLayers.clear();
      this.radarKey = key;
      this.radarIndex = radar.frames.length - 1;
    }

    if (radar.playing) this.startPlaying(radar);
    else {
      this.stopPlaying();
      this.radarIndex = radar.frames.length - 1; // the newest frame
    }
    this.showRadarFrame(radar, this.radarIndex);
  }

  private showRadarFrame(radar: WeatherState['radar'], index: number): void {
    const frame = radar.frames[index];
    if (!frame) return;

    // Layers are made on first use and kept, so playing back is smooth after the first lap
    if (!this.radarLayers.has(index)) {
      this.radarLayers.set(
        index,
        L.tileLayer(`${radar.host}${frame.path}/256/{z}/{x}/{y}/2/1_1.png`, {
          pane: 'weather',
          opacity: 0,
          maxNativeZoom: 7, // RainViewer serves nothing sharper; the map stretches the last level
          maxZoom: 19,
          attribution: '<a href="https://www.rainviewer.com/">RainViewer</a>'
        }).addTo(this.map)
      );
    }

    this.radarLayers.forEach((layer, i) => layer.setOpacity(i === index ? RADAR_OPACITY : 0));
    this.onRadarFrame(frame);
  }

  private startPlaying(radar: WeatherState['radar']): void {
    if (this.playTimer) return;

    this.playTimer = setInterval(() => {
      this.radarIndex = (this.radarIndex + 1) % radar.frames.length;
      this.showRadarFrame(radar, this.radarIndex);
    }, FRAME_MS);
  }

  private stopPlaying(): void {
    if (this.playTimer) clearInterval(this.playTimer);
    this.playTimer = undefined;
  }

  // ---- satellite rainfall ----

  private syncSatellite(on: boolean): void {
    if (!on) {
      this.satellite?.remove();
      this.satellite = undefined;
      return;
    }
    if (this.satellite) return;

    this.satellite = L.tileLayer(NASA_RAIN_TILES, {
      pane: 'weather',
      opacity: 0.6,
      maxNativeZoom: 6, // the satellite data is coarse (about 10 km), so this is all there is
      maxZoom: 19,
      attribution: 'Rainfall: <a href="https://gpm.nasa.gov/data/imerg">NASA IMERG</a> via GIBS'
    }).addTo(this.map);
  }

  // ---- typhoons ----

  private syncTyphoons(storms: Typhoon[] | null): void {
    if (storms === null) {
      this.storms.clearLayers();
      this.storms.remove();
      this.stormBounds.clear();
      this.stormsKey = '';
      return;
    }

    // Redraw only when the data actually changed
    const key = JSON.stringify(storms.map(s => [s.id, s.lat, s.lng, s.alertLevel, s.track.length]));
    if (key === this.stormsKey) return;
    this.stormsKey = key;

    this.storms.clearLayers();
    this.stormBounds.clear();
    if (!this.map.hasLayer(this.storms)) this.storms.addTo(this.map);

    for (const storm of storms) this.drawStorm(storm);
  }

  private drawStorm(storm: Typhoon): void {
    const color = ALERT_COLOR[storm.alertLevel] ?? ALERT_COLOR['Green'];
    const bounds = L.latLngBounds([[storm.lat, storm.lng]]);

    // Widest first, so the stronger-wind areas sit on top
    for (const area of [...storm.windAreas].sort((a, b) => a.kmPerHour - b.kmPerHour)) {
      const shade = WIND_COLOR[area.kmPerHour] ?? color;
      L.polygon(area.ring, { color: shade, weight: 1, fillColor: shade, fillOpacity: 0.1, interactive: false }).addTo(
        this.storms
      );
    }

    // Where the centre may go: the "cone of uncertainty"
    for (const ring of storm.cone) {
      const cone = L.polygon(ring, {
        color,
        weight: 2,
        dashArray: '6 6',
        fillColor: color,
        fillOpacity: 0.12,
        interactive: false
      });
      cone.addTo(this.storms);
      bounds.extend(cone.getBounds());
    }

    for (const segment of storm.track) {
      L.polyline(segment.points, {
        color: CATEGORY_COLOR[segment.category] ?? color,
        weight: 3,
        opacity: 0.9,
        interactive: false
      }).addTo(this.storms);
    }

    for (const point of storm.forecastPoints) {
      L.circleMarker([point.lat, point.lng], {
        radius: 3,
        color: '#0b1f33',
        weight: 1,
        fillColor: '#ffffff',
        fillOpacity: 1
      })
        .bindTooltip(point.label)
        .addTo(this.storms);
    }

    const marker = L.marker([storm.lat, storm.lng], {
      icon: L.divIcon({
        className: 'typhoon-marker',
        html: `<span class="typhoon-marker__eye" style="--alert:${color}">🌀</span>`,
        iconSize: [40, 40],
        iconAnchor: [20, 20]
      }),
      zIndexOffset: 1000
    }).bindPopup(this.popupFor(storm));
    marker.addTo(this.storms);

    this.stormBounds.set(storm.id, bounds);
  }

  // Built from DOM nodes: the storm's name and country come from an outside service
  private popupFor(storm: Typhoon): HTMLElement {
    const root = document.createElement('div');
    root.className = 'typhoon-popup';

    const title = document.createElement('strong');
    title.textContent = storm.name;
    const alert = document.createElement('p');
    alert.textContent = `${storm.alertLevel} alert${storm.country ? ' · ' + storm.country : ''}`;
    const source = document.createElement('p');
    source.className = 'typhoon-popup__muted';
    source.textContent = `Forecast from ${storm.source ?? 'JTWC'} via GDACS. Not an official PAGASA product.`;

    root.append(title, alert, source, this.link('PAGASA cyclone bulletin ↗', PAGASA_LINKS.cycloneBulletin));
    if (storm.reportUrl && /^https:\/\//.test(storm.reportUrl))
      root.append(this.link('GDACS report ↗', storm.reportUrl));
    return root;
  }

  private link(text: string, href: string): HTMLAnchorElement {
    const a = document.createElement('a');
    a.href = href;
    a.target = '_blank';
    a.rel = 'noopener noreferrer';
    a.textContent = text;
    return a;
  }
}
