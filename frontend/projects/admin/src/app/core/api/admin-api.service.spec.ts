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
});
