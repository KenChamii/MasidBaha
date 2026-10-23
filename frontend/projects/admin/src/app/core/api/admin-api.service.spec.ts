import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '@masidbaha/shared';
import { AdminApiService } from './admin-api.service';

describe('AdminApiService', () => {
  let api: AdminApiService;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_BASE_URL, useValue: 'https://api.test' },
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });
    api = TestBed.inject(AdminApiService);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('leaves empty filters off the URL', () => {
    api
      .getReports({
        page: 2,
        pageSize: 25,
        search: '',
        status: null,
        severity: 3,
        city: '  '.trim(),
        sortBy: 'confidence',
        descending: false
      })
      .subscribe();

    const req = backend.expectOne(r => r.url === 'https://api.test/api/v1/admin/reports');
    expect(req.request.params.keys().sort()).toEqual(['descending', 'page', 'pageSize', 'severity', 'sortBy']);
    expect(req.request.params.get('descending')).toBe('false');
    req.flush({ reports: [], totalCount: 0, page: 2, pageSize: 25 });
  });

  it('sends bulk actions as ids plus an action', () => {
    api.bulk(['a', 'b'], 5).subscribe();

    const req = backend.expectOne('https://api.test/api/v1/admin/moderation/bulk');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ ids: ['a', 'b'], action: 5 });
    req.flush({ action: 5, affectedIds: ['a', 'b'], notFoundIds: [] });
  });

  it('asks for the CSV as a file, with the same filters', () => {
    api.exportReports({ status: 1 }).subscribe();

    const req = backend.expectOne(r => r.url === 'https://api.test/api/v1/admin/reports/export');
    expect(req.request.responseType).toBe('blob');
    expect(req.request.params.get('status')).toBe('1');
    req.flush(new Blob(['id']));
  });

  it('escapes session ids in the trust URL', () => {
    api.getSessionTrust('a/b c').subscribe();

    backend.expectOne('https://api.test/api/v1/admin/sessions/a%2Fb%20c/trust').flush({});
  });

  it('sends an announcement to everyone when no area is chosen', () => {
    api.sendBroadcast('Typhoon', 'Stay safe', '/map').subscribe();

    const req = backend.expectOne('https://api.test/api/v1/admin/broadcasts');
    expect(req.request.body).toEqual({
      title: 'Typhoon',
      body: 'Stay safe',
      url: '/map',
      areaLat: null,
      areaLng: null,
      areaRadiusMeters: null
    });
    req.flush({});
  });

  it('limits an announcement to a circle when an area is chosen', () => {
    api.sendBroadcast('Typhoon', 'Metro Manila only', '/map', { lat: 14.6, lng: 121, radiusMeters: 30000 }).subscribe();

    const req = backend.expectOne('https://api.test/api/v1/admin/broadcasts');
    expect(req.request.body).toMatchObject({ areaLat: 14.6, areaLng: 121, areaRadiusMeters: 30000 });
    req.flush({});
  });
});
