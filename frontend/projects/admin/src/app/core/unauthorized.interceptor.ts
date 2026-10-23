import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { AdminAuthService } from '@masidbaha/shared';
import { catchError, throwError } from 'rxjs';

// If the API says the session is gone (expired, signed out elsewhere, password
// changed), drop back to the login page. The login and "who am I" calls are
// excluded: a 401 there is an answer, not a lost session.
export const unauthorizedInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AdminAuthService);
  const router = inject(Router);
  const isAuthCall = request.url.includes('/api/v1/auth/');

  return next(request).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401 && !isAuthCall && auth.isSignedIn()) {
        auth.clearLocalSession();
        void router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
      }
      return throwError(() => error);
    })
  );
};
