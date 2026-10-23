import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { AdminAuthService, AdminRole, AdminUser } from '@masidbaha/shared';
import { Observable, of } from 'rxjs';
import { authGuard, guestGuard, roleGuard } from './auth.guard';

const user = (...roles: AdminRole[]): AdminUser => ({ id: '1', email: 'a@test.local', displayName: 'A', roles });

// A stand-in for the real service: the guards only need these members
class FakeAuth {
  current: AdminUser | null = null;
  ensureRestored = () => of(this.current);
  hasAnyRole = (...roles: AdminRole[]) => !!this.current && this.current.roles.some(r => roles.includes(r));
}

describe('route guards', () => {
  let auth: FakeAuth;
  let router: Router;

  beforeEach(() => {
    auth = new FakeAuth();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AdminAuthService, useValue: auth }]
    });
    router = TestBed.inject(Router);
  });

  const state = (url: string) => ({ url }) as RouterStateSnapshot;
  const route = {} as ActivatedRouteSnapshot;

  function run(guard: () => unknown): boolean | UrlTree {
    let result: boolean | UrlTree | undefined;
    (TestBed.runInInjectionContext(guard) as Observable<boolean | UrlTree>).subscribe(r => (result = r));
    return result as boolean | UrlTree;
  }

  describe('authGuard', () => {
    it('lets a signed-in user through', () => {
      auth.current = user('Viewer');

      expect(run(() => authGuard(route, state('/reports')))).toBe(true);
    });

    it('sends everyone else to the login page, remembering where they were going', () => {
      const result = run(() => authGuard(route, state('/reports?page=2')));

      expect(result).toBeInstanceOf(UrlTree);
      expect(router.serializeUrl(result as UrlTree)).toBe('/login?returnUrl=%2Freports%3Fpage%3D2');
    });
  });

  describe('guestGuard', () => {
    it('shows the login page to someone who is not signed in', () => {
      expect(run(() => guestGuard(route, state('/login')))).toBe(true);
    });

    it('sends a signed-in user to the app instead', () => {
      auth.current = user('Moderator');

      const result = run(() => guestGuard(route, state('/login')));

      expect(router.serializeUrl(result as UrlTree)).toBe('/');
    });
  });

  describe('roleGuard', () => {
    it('allows a matching role', () => {
      auth.current = user('SuperAdmin');

      expect(run(() => roleGuard('SuperAdmin')(route, state('/users')))).toBe(true);
    });

    it('turns away other roles', () => {
      auth.current = user('Moderator');

      const result = run(() => roleGuard('SuperAdmin')(route, state('/users')));

      expect(router.serializeUrl(result as UrlTree)).toBe('/');
    });

    it('accepts any of several roles', () => {
      auth.current = user('Moderator');

      expect(run(() => roleGuard('Moderator', 'SuperAdmin')(route, state('/x')))).toBe(true);
    });
  });
});
