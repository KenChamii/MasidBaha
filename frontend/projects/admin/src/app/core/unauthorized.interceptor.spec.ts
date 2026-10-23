import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { API_BASE_URL } from '@masidbaha/shared';
import { AdminAuthService } from './api/admin-auth.service';
import { unauthorizedInterceptor } from './unauthorized.interceptor';

describe('unauthorizedInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let auth: AdminAuthService;
  let navigate: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '' },
        provideHttpClient(withInterceptors([unauthorizedInterceptor])),
        provideHttpClientTesting()
      ]
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AdminAuthService);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    auth.user.set({
      id: '1',
      email: 'a@test.local',
      displayName: 'A',
      roles: ['Moderator'],
      mustChangePassword: false,
      twoFactorEnabled: false
    });
  });

  function get(url: string, status: number): void {
    http.get(url).subscribe({ error: () => undefined });
    backend.expectOne(url).flush(null, { status, statusText: 'x' });
  }

  it('sends a signed-in user to the login page when the session has ended', () => {
    get('/api/v1/admin/reports', 401);

    expect(auth.isSignedIn()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/login'], expect.objectContaining({ queryParams: expect.any(Object) }));
  });

  it('leaves 401s from the sign-in calls alone: that is an answer, not a lost session', () => {
    get('/api/v1/auth/login', 401);
    get('/api/v1/auth/me', 401);

    expect(auth.isSignedIn()).toBe(true);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('ignores other failures', () => {
    get('/api/v1/admin/reports', 403);
    get('/api/v1/admin/reports', 500);

    expect(auth.isSignedIn()).toBe(true);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('does nothing when nobody was signed in', () => {
    auth.clearLocalSession();

    get('/api/v1/admin/reports', 401);

    expect(navigate).not.toHaveBeenCalled();
  });

  it('still passes the error on to the caller', () => {
    let status: number | undefined;
    http.get('/api/v1/admin/reports').subscribe({ error: e => (status = e.status) });

    backend.expectOne('/api/v1/admin/reports').flush(null, { status: 401, statusText: 'x' });

    expect(status).toBe(401);
  });
});
