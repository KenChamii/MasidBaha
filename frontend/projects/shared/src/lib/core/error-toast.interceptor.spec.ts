import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ToastService } from '../ui/toast/toast.service';
import { SKIP_ERROR_TOAST, errorToastInterceptor } from './error-toast.interceptor';

describe('errorToastInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let toasts: ToastService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([errorToastInterceptor])), provideHttpClientTesting()]
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    toasts = TestBed.inject(ToastService);
  });

  afterEach(() => vi.unstubAllGlobals());

  // Fires a request that fails, and returns the status the caller saw
  function fail(status: number, context?: HttpContext): { status?: number } {
    const seen: { status?: number } = {};
    http.get('/api/thing', { context }).subscribe({ error: e => (seen.status = e.status) });
    backend.expectOne('/api/thing').flush(null, { status, statusText: 'x' });
    return seen;
  }

  it('toasts server errors, and still hands the error to the caller', () => {
    const seen = fail(500);

    expect(toasts.toasts()[0].message).toMatch(/something went wrong/i);
    expect(seen.status).toBe(500);
  });

  it('toasts rate limiting', () => {
    fail(429);

    expect(toasts.toasts()[0].message).toMatch(/too often/i);
  });

  it('stays quiet for 4xx answers that the caller handles itself', () => {
    fail(400);
    fail(401);
    fail(404);
    fail(409);

    expect(toasts.toasts()).toHaveLength(0);
  });

  it('toasts an unreachable server', () => {
    vi.stubGlobal('navigator', { onLine: true });
    http.get('/api/thing').subscribe({ error: () => undefined });

    backend.expectOne('/api/thing').error(new ProgressEvent('error'), { status: 0 });

    expect(toasts.toasts()[0].message).toMatch(/reach the server/i);
  });

  it('does not repeat the offline banner as a toast', () => {
    vi.stubGlobal('navigator', { onLine: false });
    http.get('/api/thing').subscribe({ error: () => undefined });

    backend.expectOne('/api/thing').error(new ProgressEvent('error'), { status: 0 });

    expect(toasts.toasts()).toHaveLength(0);
  });

  it('can be switched off per request', () => {
    fail(500, new HttpContext().set(SKIP_ERROR_TOAST, true));

    expect(toasts.toasts()).toHaveLength(0);
  });

  it('shows one toast when several requests fail together', () => {
    fail(500);
    fail(500);
    fail(503);

    expect(toasts.toasts()).toHaveLength(1);
  });
});
