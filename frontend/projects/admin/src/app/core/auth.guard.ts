import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { AdminAuthService } from './api/admin-auth.service';
import { AdminRole } from './api/admin.models';

// Lets signed-in staff through. Anyone else is sent to the login page, which
// brings them back to where they were headed afterwards. Someone still on a
// temporary password can only reach the account page, where they replace it.
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AdminAuthService);
  const router = inject(Router);

  return auth.ensureRestored().pipe(
    map(user => {
      if (!user) return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
      if (user.mustChangePassword && !state.url.startsWith('/account')) return router.createUrlTree(['/account']);
      return true;
    })
  );
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
