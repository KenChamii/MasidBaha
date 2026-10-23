import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { RadarFrames, Typhoon, TyphoonsResponse, WeatherService } from '../../../core/services/weather.service';
import { WeatherLayersStore } from './weather-layers.store';

const radar: RadarFrames = {
  host: 'https://tiles.example',
  frames: [
    { time: 100, path: '/a' },
    { time: 200, path: '/b' }
  ]
};
const storm: Typhoon = {
  id: 1,
  name: 'TESTO-26',
  alertLevel: 'Red',
  source: 'JTWC',
  country: null,
  reportUrl: null,
  lat: 22.7,
  lng: 123.8,
  track: [],
  cone: [],
  windAreas: [],
  forecastPoints: []
};
const typhoons: TyphoonsResponse = { generatedAt: '2026-10-23T09:00:00Z', stale: false, storms: [storm] };

describe('WeatherLayersStore', () => {
  let getRadarFrames: ReturnType<typeof vi.fn<() => Observable<RadarFrames>>>;
  let getTyphoons: ReturnType<typeof vi.fn<() => Observable<TyphoonsResponse>>>;

  function create(saved?: object) {
    localStorage.clear();
    if (saved) localStorage.setItem('masidbaha-weather', JSON.stringify(saved));

    getRadarFrames = vi.fn(() => of(radar));
    getTyphoons = vi.fn(() => of(typhoons));
    TestBed.configureTestingModule({
      providers: [WeatherLayersStore, { provide: WeatherService, useValue: { getRadarFrames, getTyphoons } }]
    });
    return TestBed.inject(WeatherLayersStore);
  }

  afterEach(() => {
    vi.useRealTimers();
    localStorage.clear();
  });

  it('starts with every layer off and fetches nothing', () => {
    const store = create();

    expect(store.anyOn()).toBe(false);
    expect(store.state().typhoons).toBeNull();
    expect(getRadarFrames).not.toHaveBeenCalled();
    expect(getTyphoons).not.toHaveBeenCalled();
  });

  it('loads the radar frames only when radar is switched on', () => {
    const store = create();

    store.toggleRadar();

    expect(getRadarFrames).toHaveBeenCalledTimes(1);
    expect(store.state().radar).toMatchObject({ on: true, host: radar.host, playing: false });
    expect(store.state().radar.frames).toHaveLength(2);
  });

  it('stops playing when the radar is switched off', () => {
    const store = create();
    store.toggleRadar();
    store.togglePlaying();
    expect(store.playing()).toBe(true);

    store.toggleRadar();

    expect(store.playing()).toBe(false);
  });

  it('hands the map the storms only while the typhoon layer is on', () => {
    const store = create();

    store.toggleTyphoons();
    expect(store.state().typhoons).toEqual([storm]);

    store.toggleTyphoons();
    expect(store.state().typhoons).toBeNull();
  });

  it('does not fetch typhoons again once they are loaded', () => {
    const store = create();

    store.toggleTyphoons();
    store.toggleTyphoons();
    store.toggleTyphoons();

    expect(getTyphoons).toHaveBeenCalledTimes(1);
  });

  it('remembers which layers were on, for the next visit', () => {
    const store = create();
    store.toggleRadar();
    store.toggleSatellite();

    expect(JSON.parse(localStorage.getItem('masidbaha-weather')!)).toEqual({
      radar: true,
      satellite: true,
      typhoons: false
    });
  });

  it('switches remembered layers back on', () => {
    const store = create({ radar: true, satellite: false, typhoons: true });

    expect(store.radarOn()).toBe(true);
    expect(store.typhoonsOn()).toBe(true);
    expect(getRadarFrames).toHaveBeenCalled();
    expect(getTyphoons).toHaveBeenCalled();
  });

  it('copes with unreadable saved settings', () => {
    localStorage.clear();
    localStorage.setItem('masidbaha-weather', '{not json');
    TestBed.configureTestingModule({
      providers: [
        WeatherLayersStore,
        { provide: WeatherService, useValue: { getRadarFrames: vi.fn(), getTyphoons: vi.fn() } }
      ]
    });

    expect(TestBed.inject(WeatherLayersStore).anyOn()).toBe(false);
  });

  it('says typhoon data is unavailable, and asks again after the server was still loading', () => {
    vi.useFakeTimers();
    const store = create();
    getTyphoons.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 503 })));

    store.toggleTyphoons();
    expect(store.typhoonStatus()).toBe('unavailable');

    vi.advanceTimersByTime(21_000);

    expect(getTyphoons).toHaveBeenCalledTimes(2);
    expect(store.typhoonStatus()).toBe('ready');
  });

  it('does not keep retrying after the layer is switched off', () => {
    vi.useFakeTimers();
    const store = create();
    getTyphoons.mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 503 })));
    store.toggleTyphoons();

    store.toggleTyphoons();
    vi.advanceTimersByTime(60_000);

    expect(getTyphoons).toHaveBeenCalledTimes(1);
  });

  it('reports radar as unavailable when the service fails', () => {
    const store = create();
    getRadarFrames.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));

    store.toggleRadar();

    expect(store.radarStatus()).toBe('unavailable');
  });
});
