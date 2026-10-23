import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { SKIP_ERROR_TOAST } from '@masidbaha/shared';
import { Observable, map } from 'rxjs';
import { environment } from '../../../environments/environment';

// ---- typhoons (our API, which refreshes GDACS / JTWC data in the background) ----

export interface Typhoon {
  id: number;
  name: string;
  alertLevel: 'Green' | 'Orange' | 'Red' | string;
  source: string | null;
  country: string | null;
  reportUrl: string | null;
  lat: number;
  lng: number;
  // [lat, lng] pairs
  track: { category: string; points: [number, number][] }[];
  cone: [number, number][][];
  windAreas: { kmPerHour: number; ring: [number, number][] }[];
  forecastPoints: { label: string; lat: number; lng: number }[];
}

export interface TyphoonsResponse {
  generatedAt: string;
  stale: boolean;
  storms: Typhoon[];
}

// ---- rain radar (RainViewer: free, no key) ----

export interface RadarFrame {
  time: number; // unix seconds
  path: string;
}

export interface RadarFrames {
  host: string;
  frames: RadarFrame[];
}

interface RainViewerIndex {
  host: string;
  radar: { past: RadarFrame[]; nowcast?: RadarFrame[] };
}

// Where the third-party layers come from. Tiles are fetched by the browser straight from these services.
export const RAINVIEWER_INDEX_URL = 'https://api.rainviewer.com/public/weather-maps.json';

// NASA GIBS: IMERG satellite rain-rate estimates. "default" always means the newest available time.
export const NASA_RAIN_TILES =
  'https://gibs.earthdata.nasa.gov/wmts/epsg3857/best/IMERG_Precipitation_Rate/default/GoogleMapsCompatible_Level6/{z}/{y}/{x}.png';

// PAGASA publishes no map data service, so people are sent to its official pages for warnings
export const PAGASA_LINKS = {
  cycloneBulletin: 'https://www.pagasa.dost.gov.ph/tropical-cyclone/severe-weather-bulletin',
  weather: 'https://www.pagasa.dost.gov.ph/weather'
};

@Injectable({ providedIn: 'root' })
export class WeatherService {
  private readonly http = inject(HttpClient);

  // A failure here only means the overlay stays off; the page says so itself, so no extra toast
  private readonly quiet = new HttpContext().set(SKIP_ERROR_TOAST, true);

  getTyphoons(): Observable<TyphoonsResponse> {
    return this.http.get<TyphoonsResponse>(`${environment.apiUrl}/api/v1/weather/typhoons`, { context: this.quiet });
  }

  getRadarFrames(): Observable<RadarFrames> {
    return this.http
      .get<RainViewerIndex>(RAINVIEWER_INDEX_URL, { context: this.quiet })
      .pipe(map(index => ({ host: index.host, frames: index.radar.past ?? [] })));
  }
}
