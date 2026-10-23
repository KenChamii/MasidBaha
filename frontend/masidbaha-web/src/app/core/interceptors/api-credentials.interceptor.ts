import { HttpInterceptorFn } from '@angular/common/http';
import { environment } from '../../../environments/environment';

// The API identifies each visitor with a signed HttpOnly session cookie, and
// the browser only sends cookies on cross-origin requests when asked to.
export const apiCredentialsInterceptor: HttpInterceptorFn = (request, next) => {
  const isApiCall = environment.apiUrl
    ? request.url.startsWith(environment.apiUrl)
    : request.url.startsWith('/');

  return next(isApiCall ? request.clone({ withCredentials: true }) : request);
};
