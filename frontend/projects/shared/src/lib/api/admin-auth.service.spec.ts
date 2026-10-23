import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from './api-config';
import { AdminAuthService, AdminUser } from './admin-auth.service';

const moderator: AdminUser = { id: '1', email: 'm@test.local', displayName: 'Mod', roles: ['Moderator'] };
const viewer: AdminUser = { id: '2', email: 'v@test.local', displayName: 'View', roles: ['Viewer'] };

describe('AdminAuthService', () => {
  let service: AdminAuthService;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        { provide: API_BASE_URL, useValue: 'https://api.test' },
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });
    service = TestBed.inject(AdminAuthService);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('starts signed out', () => {
    expect(service.user()).toBeNull();
    expect(service.isSignedIn()).toBe(false);
    expect(service.canModerate()).toBe(false);
  });

  it('restores the session from the API', () => {
    service.restore().subscribe();

    backend.expectOne('https://api.test/api/v1/auth/me').flush(moderator);

    expect(service.user()).toEqual(moderator);
    expect(service.isSignedIn()).toBe(true);
  });

  it('treats a 401 from /me as simply signed out', () => {
    let result: AdminUser | null | undefined;
    service.restore().subscribe(u => (result = u));

    backend.expectOne('https://api.test/api/v1/auth/me').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(result).toBeNull();
    expect(service.isSignedIn()).toBe(false);
  });

  it('asks the API only once per page load when guards call ensureRestored', () => {
    service.ensureRestored().subscribe();
    backend.expectOne('https://api.test/api/v1/auth/me').flush(moderator);

    let second: AdminUser | null | undefined;
    service.ensureRestored().subscribe(u => (second = u));

    backend.expectNone('https://api.test/api/v1/auth/me');
    expect(second).toEqual(moderator);
  });

  it('signs in and remembers the user', () => {
    service.login('m@test.local', 'secret-passphrase').subscribe();

    const req = backend.expectOne('https://api.test/api/v1/auth/login');
    expect(req.request.body).toEqual({ email: 'm@test.local', password: 'secret-passphrase' });
    req.flush(moderator);

    expect(service.user()).toEqual(moderator);
  });

  it('does not touch the session when sign-in fails', () => {
    service.login('m@test.local', 'wrong').subscribe({ error: () => undefined });

    backend
      .expectOne('https://api.test/api/v1/auth/login')
      .flush({ detail: 'Invalid email or password.' }, { status: 401, statusText: 'x' });

    expect(service.user()).toBeNull();
  });

  it('signs out, and forgets the user even if the API call fails', () => {
    service.user.set(moderator);
    service.logout().subscribe();
    backend.expectOne('https://api.test/api/v1/auth/logout').flush(null, { status: 500, statusText: 'x' });

    expect(service.user()).toBeNull();
  });

  it('knows who may moderate', () => {
    service.user.set(viewer);
    expect(service.canModerate()).toBe(false);
    expect(service.hasAnyRole('Viewer')).toBe(true);

    service.user.set(moderator);
    expect(service.canModerate()).toBe(true);

    service.user.set({ ...viewer, roles: ['SuperAdmin'] });
    expect(service.canModerate()).toBe(true);
  });

  it('clears the local session without a request', () => {
    service.user.set(moderator);

    service.clearLocalSession();

    expect(service.isSignedIn()).toBe(false);
    backend.expectNone('https://api.test/api/v1/auth/me');
  });
});
