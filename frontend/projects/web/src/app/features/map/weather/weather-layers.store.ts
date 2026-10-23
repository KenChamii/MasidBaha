import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { RadarFrame, RadarFrames, TyphoonsResponse, WeatherService } from '../../../core/services/weather.service';
import { WeatherState } from './weather-overlays';

export type LoadStatus = 'idle' | 'loading' | 'ready' | 'unavailable';

const STORAGE_KEY = 'masidbaha-weather';
const RADAR_REFRESH_MS = 10 * 60 * 1000;
// The first typhoon download after the API wakes up takes about half a minute: ask again
const TYPHOON_RETRY_MS = 20 * 1000;

interface Saved {
  radar: boolean;
  satellite: boolean;
  typhoons: boolean;
}

// Which weather layers are on, and the data behind them. Created with the map page (not app-wide), so
// its refresh timers stop when the page does. The choice of layers is remembered between visits.
@Injectable()
export class WeatherLayersStore {
  private readonly weather = inject(WeatherService);

  readonly radarOn = signal(false);
  readonly satelliteOn = signal(false);
  readonly typhoonsOn = signal(false);
  readonly playing = signal(false);

  readonly radar = signal<RadarFrames | null>(null);
  readonly radarStatus = signal<LoadStatus>('idle');
  // The frame the map is showing right now (reported back by the overlay)
  readonly radarFrame = signal<RadarFrame | null>(null);

  readonly typhoons = signal<TyphoonsResponse | null>(null);
  readonly typhoonStatus = signal<LoadStatus>('idle');

  readonly anyOn = computed(() => this.radarOn() || this.satelliteOn() || this.typhoonsOn());

  // Everything the map overlay needs, in one value
  readonly state = computed<WeatherState>(() => ({
    radar: {
      on: this.radarOn(),
      host: this.radar()?.host ?? '',
      frames: this.radar()?.frames ?? [],
      playing: this.playing()
    },
    satellite: this.satelliteOn(),
    typhoons: this.typhoonsOn() ? (this.typhoons()?.storms ?? []) : null
  }));

  private typhoonRetry?: ReturnType<typeof setTimeout>;

  constructor() {
    const refresh = setInterval(() => {
      if (this.radarOn()) this.loadRadar();
    }, RADAR_REFRESH_MS);

    inject(DestroyRef).onDestroy(() => {
      clearInterval(refresh);
      clearTimeout(this.typhoonRetry);
    });

    const saved = this.read();
    if (saved.radar) this.toggleRadar();
    if (saved.satellite) this.satelliteOn.set(true);
    if (saved.typhoons) this.toggleTyphoons();
  }

  toggleRadar(): void {
    const on = !this.radarOn();
    this.radarOn.set(on);
    if (!on) this.playing.set(false);
    else if (!this.radar()) this.loadRadar();
    this.save();
  }

  toggleSatellite(): void {
    this.satelliteOn.update(on => !on);
    this.save();
  }

  toggleTyphoons(): void {
    const on = !this.typhoonsOn();
    this.typhoonsOn.set(on);
    if (on && this.typhoonStatus() !== 'ready') this.loadTyphoons();
    this.save();
  }

  togglePlaying(): void {
    this.playing.update(on => !on);
  }

  loadRadar(): void {
    if (this.radarStatus() === 'loading') return;
    this.radarStatus.set('loading');

    this.weather.getRadarFrames().subscribe({
      next: frames => {
        this.radar.set(frames);
        this.radarStatus.set(frames.frames.length ? 'ready' : 'unavailable');
      },
      error: () => this.radarStatus.set(this.radar() ? 'ready' : 'unavailable')
    });
  }

  loadTyphoons(): void {
    if (this.typhoonStatus() === 'loading') return;
    clearTimeout(this.typhoonRetry);
    this.typhoonStatus.set('loading');

    this.weather.getTyphoons().subscribe({
      next: data => {
        this.typhoons.set(data);
        this.typhoonStatus.set('ready');
      },
      error: (error: HttpErrorResponse) => {
        this.typhoonStatus.set('unavailable');
        // 503 = the server is still fetching its first copy: try again shortly while the layer is on
        if (error.status === 503) {
          this.typhoonRetry = setTimeout(() => {
            if (this.typhoonsOn()) this.loadTyphoons();
          }, TYPHOON_RETRY_MS);
        }
      }
    });
  }

  // ---- remembered between visits (never required: private windows block storage) ----

  private read(): Saved {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      const parsed = raw ? (JSON.parse(raw) as Partial<Saved>) : {};
      return { radar: !!parsed.radar, satellite: !!parsed.satellite, typhoons: !!parsed.typhoons };
    } catch {
      return { radar: false, satellite: false, typhoons: false };
    }
  }

  private save(): void {
    try {
      const saved: Saved = { radar: this.radarOn(), satellite: this.satelliteOn(), typhoons: this.typhoonsOn() };
      localStorage.setItem(STORAGE_KEY, JSON.stringify(saved));
    } catch {
      // Not remembering the choice is fine
    }
  }
}
