import { provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { apiCredentialsInterceptor, errorToastInterceptor, provideApiBase } from '@masidbaha/shared';
import { environment } from '../environments/environment';
import { unauthorizedInterceptor } from './core/unauthorized.interceptor';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection(),
    provideRouter(routes),
    provideApiBase(environment.apiUrl),
    provideHttpClient(
      withXhr(),
      withInterceptors([apiCredentialsInterceptor, unauthorizedInterceptor, errorToastInterceptor])
    )
  ]
};
