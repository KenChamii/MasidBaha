import { HttpContextToken, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ToastService } from '../ui/toast/toast.service';

// Set on a request that handles its own failures, so no toast is added
export const SKIP_ERROR_TOAST = new HttpContextToken<boolean>(() => false);

function messageFor(error: HttpErrorResponse): string | null {
  // Offline: the app already says so itself (banner), no need to repeat it per request
  if (error.status === 0) {
    return typeof navigator !== 'undefined' && navigator.onLine === false
      ? null
      : "Can't reach the server. Please try again in a moment.";
  }

  if (error.status === 429) return 'You are doing that too often. Please wait a moment and try again.';
  if (error.status >= 500) return 'Something went wrong on our side. Please try again.';

  // 4xx answers (validation, conflicts, auth) are expected and shown where they happen
  return null;
}

// Tells the user about failures they can't fix from inside a form.
// The error is always re-thrown, so callers still handle their own cases.
export const errorToastInterceptor: HttpInterceptorFn = (request, next) => {
  const toasts = inject(ToastService);

  return next(request).pipe(
    catchError((error: HttpErrorResponse) => {
      if (!request.context.get(SKIP_ERROR_TOAST)) {
        const message = messageFor(error);
        if (message) toasts.error(message);
      }
      return throwError(() => error);
    })
  );
};
