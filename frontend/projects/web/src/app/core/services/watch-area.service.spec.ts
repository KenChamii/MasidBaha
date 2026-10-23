import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { WatchArea, WatchAreaService } from './watch-area.service';

const url = `${environment.apiUrl}/api/v1/push/watch-areas`;
const home: WatchArea = { id: 'a1', name: 'Home', lat: 14.65, lng: 121.1, radiusMeters: 2000 };

describe('WatchAreaService', () => {
  let service: WatchAreaService;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(WatchAreaService);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('loads the saved places', async () => {
    const loading = service.load();
    backend.expectOne(url).flush([home]);
    await loading;

    expect(service.areas()).toEqual([home]);
  });

  it('adds a place and keeps it in the list', async () => {
    const adding = service.add('Home', 14.65, 121.1, 2000);

    const req = backend.expectOne(url);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Home', lat: 14.65, lng: 121.1, radiusMeters: 2000 });
    req.flush(home);
    await adding;

    expect(service.areas()).toEqual([home]);
  });

  it('leaves the list alone when the server refuses', async () => {
    const adding = service.add('Home', 14.65, 121.1, 2000);
    backend.expectOne(url).flush({ detail: 'Turn on alerts first.' }, { status: 409, statusText: 'Conflict' });

    await expect(adding).rejects.toBeTruthy();
    expect(service.areas()).toEqual([]);
  });

  it('removes a place', async () => {
    service.areas.set([home]);

    const removing = service.remove('a1');
    const req = backend.expectOne(`${url}/a1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
    await removing;

    expect(service.areas()).toEqual([]);
  });

  it('forgets everything when alerts are turned off', () => {
    service.areas.set([home]);

    service.clear();

    expect(service.areas()).toEqual([]);
  });
});
