import { TestBed } from '@angular/core/testing';
import { FloodReport, FloodReportService, TopReportsQuery } from '@masidbaha/shared';
import { Observable, of, throwError } from 'rxjs';
import { FloodReportsStore } from './flood-reports.store';

const report = (id: string, region?: string, province?: string, city?: string): FloodReport => ({
  id,
  lat: 14.6,
  lng: 121.1,
  severity: 2,
  reportedAt: '2026-10-23T00:00:00Z',
  confidenceScore: 1,
  status: 1,
  region,
  province,
  city
});

const SAMPLE = [
  report('1', 'NCR', 'Metro Manila', 'Marikina'),
  report('2', 'NCR', 'Metro Manila', 'Quezon City'),
  report('3', 'Calabarzon', 'Laguna', 'Cabuyao'),
  report('4', 'Calabarzon', 'Laguna', 'Cabuyao'),
  report('5')
];

describe('FloodReportsStore', () => {
  let getTop: ReturnType<typeof vi.fn<(query: TopReportsQuery) => Observable<FloodReport[]>>>;
  let store: FloodReportsStore;

  beforeEach(() => {
    getTop = vi.fn().mockReturnValue(of(SAMPLE));
    TestBed.configureTestingModule({
      providers: [FloodReportsStore, { provide: FloodReportService, useValue: { getTop } }]
    });
    store = TestBed.inject(FloodReportsStore);
  });

  it('loads a broad sample for the dropdowns and the current top list on init', () => {
    store.init();

    expect(getTop).toHaveBeenCalledWith({ limit: 200 });
    expect(getTop).toHaveBeenCalledWith(expect.objectContaining({ limit: 20 }));
    expect(store.topReports()).toHaveLength(SAMPLE.length);
    expect(store.isLoading()).toBe(false);
  });

  it('offers each region once, sorted, ignoring reports without a place', () => {
    store.init();

    expect(store.availableRegions()).toEqual(['Calabarzon', 'NCR']);
  });

  it('narrows provinces and cities to the chosen parent', () => {
    store.init();

    store.setRegion('NCR');
    expect(store.availableProvinces()).toEqual(['Metro Manila']);
    expect(store.availableCities()).toEqual(['Marikina', 'Quezon City']);

    store.setRegion('Calabarzon');
    expect(store.availableCities()).toEqual(['Cabuyao']);
  });

  it('asks for the selected place when refreshing the top list', () => {
    store.init();

    store.setRegion('NCR');
    store.setProvince('Metro Manila');
    store.setCity('Marikina');

    expect(getTop).toHaveBeenLastCalledWith({
      region: 'NCR',
      province: 'Metro Manila',
      city: 'Marikina',
      limit: 20
    });
  });

  it('clears the lower levels when a higher one changes', () => {
    store.setRegion('NCR');
    store.setProvince('Metro Manila');
    store.setCity('Marikina');

    store.setRegion('Calabarzon');

    expect(store.selectedProvince()).toBeNull();
    expect(store.selectedCity()).toBeNull();

    store.setProvince('Laguna');
    store.setCity('Cabuyao');
    store.setProvince('Laguna');

    expect(store.selectedCity()).toBeNull();
  });

  it('treats an empty choice as no filter', () => {
    store.setRegion('NCR');

    store.setRegion('');

    expect(store.selectedRegion()).toBeNull();
  });

  it('goes back to national and forgets every selection', () => {
    store.setRegion('NCR');
    store.setProvince('Metro Manila');
    store.setScope('region');
    expect(store.selectedRegion()).toBe('NCR'); // other scopes keep their selection

    store.setScope('national');

    expect(store.scope()).toBe('national');
    expect(store.selectedRegion()).toBeNull();
    expect(store.selectedProvince()).toBeNull();
    expect(store.selectedCity()).toBeNull();
  });

  it('stops showing the loading state when the request fails', () => {
    getTop.mockReturnValue(throwError(() => new Error('down')));

    store.refresh();

    expect(store.isLoading()).toBe(false);
  });
});
