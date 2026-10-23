import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AdminAuthService, AdminRole } from '@masidbaha/shared';
import { map } from 'rxjs';

// Lets signed-in staff through. Anyone else is sent to the login page, which
// brings them back to where they were headed afterwards.
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AdminAuthService);
  const router = inject(Router);

  return auth
    .ensureRestored()
    .pipe(map(user => (user ? true : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } }))));
};

// The login page is for people who are not signed in yet
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AdminAuthService);
  const router = inject(Router);

  return auth.ensureRestored().pipe(map(user => (user ? router.createUrlTree(['/']) : true)));
};

// For areas limited to certain roles (e.g. managing staff accounts)
export const roleGuard =
  (...roles: AdminRole[]): CanActivateFn =>
  () => {
    const auth = inject(AdminAuthService);
    const router = inject(Router);

    return auth.ensureRestored().pipe(map(() => (auth.hasAnyRole(...roles) ? true : router.createUrlTree(['/']))));
  };
