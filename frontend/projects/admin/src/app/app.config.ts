import { provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import {
  ApplicationConfig,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  provideZoneChangeDetection
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { apiCredentialsInterceptor, errorToastInterceptor, provideApiBase } from '@masidbaha/shared';
import { environment } from '../environments/environment';
import { unauthorizedInterceptor } from './core/unauthorized.interceptor';
import { routes } from './app.routes';
import { watchTables } from './ui/stack-tables';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideAppInitializer(() => {
      watchTables();
    }),
    provideZoneChangeDetection(),
    provideRouter(routes, withComponentInputBinding()),
    provideApiBase(environment.apiUrl),
    provideHttpClient(
      withXhr(),
      withInterceptors([apiCredentialsInterceptor, unauthorizedInterceptor, errorToastInterceptor])
    )
  ]
};
