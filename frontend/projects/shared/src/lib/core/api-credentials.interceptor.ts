import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { API_BASE_URL } from '../api/api-config';

// The API identifies each visitor with a signed HttpOnly session cookie, and
// the browser only sends cookies on cross-origin requests when asked to.
export const apiCredentialsInterceptor: HttpInterceptorFn = (request, next) => {
  const apiUrl = inject(API_BASE_URL);
  const isApiCall = apiUrl ? request.url.startsWith(apiUrl) : request.url.startsWith('/');

  return next(isApiCall ? request.clone({ withCredentials: true }) : request);
};
