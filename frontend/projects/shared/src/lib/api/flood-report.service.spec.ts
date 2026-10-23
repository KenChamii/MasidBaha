import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from './api-config';
import { FloodReportService } from './flood-report.service';

describe('FloodReportService', () => {
  let service: FloodReportService;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [{ provide: API_BASE_URL, useValue: '' }, provideHttpClient(), provideHttpClientTesting()]
    });
    service = TestBed.inject(FloodReportService);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('looks up nearby reports with the position and radius', () => {
    service.getNearby(14.65, 121.1, 3000).subscribe();

    const req = backend.expectOne(r => r.url === '/api/v1/flood-reports');
    expect(req.request.params.get('lat')).toBe('14.65');
    expect(req.request.params.get('lng')).toBe('121.1');
    expect(req.request.params.get('radiusMeters')).toBe('3000');
    req.flush([]);
  });

  it('only sends the top-reports filters that are set', () => {
    service.getTop({ city: 'Marikina', limit: 5 }).subscribe();

    const req = backend.expectOne(r => r.url === '/api/v1/flood-reports/top');
    expect(req.request.params.get('city')).toBe('Marikina');
    expect(req.request.params.get('limit')).toBe('5');
    expect(req.request.params.has('region')).toBe(false);
    expect(req.request.params.has('province')).toBe(false);
    req.flush([]);
  });

  it('defaults the top-reports limit to 20', () => {
    service.getTop({}).subscribe();

    const req = backend.expectOne(r => r.url === '/api/v1/flood-reports/top');
    expect(req.request.params.get('limit')).toBe('20');
    req.flush([]);
  });

  it('votes with the vote type only: the voter is the server-side session', () => {
    service.vote('abc', -1).subscribe();

    const req = backend.expectOne('/api/v1/flood-reports/abc/vote');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ voteType: -1 });
    req.flush({});
  });

  it('sends the heatmap date range and place filters', () => {
    service.getHeatmap({ fromDate: '2026-01-01', region: 'Calabarzon' }).subscribe();

    const req = backend.expectOne(r => r.url === '/api/v1/flood-reports/heatmap');
    expect(req.request.params.get('fromDate')).toBe('2026-01-01');
    expect(req.request.params.get('region')).toBe('Calabarzon');
    expect(req.request.params.has('toDate')).toBe(false);
    req.flush([]);
  });
});
