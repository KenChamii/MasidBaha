import { ApplicationConfig, inject, isDevMode, provideAppInitializer, provideZoneChangeDetection } from '@angular/core';
import { provideRouter, withInMemoryScrolling } from '@angular/router';
import { provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import { provideServiceWorker } from '@angular/service-worker';
import { routes } from './app.routes';
import { apiCredentialsInterceptor, errorToastInterceptor, provideApiBase, provideI18n } from '@masidbaha/shared';
import { WEB_MESSAGES } from './core/i18n/messages';
import { ReportQueueService } from './core/services/report-queue.service';
import { environment } from '../environments/environment';

export const appConfig: ApplicationConfig = {
  providers: [
    // Zone-based change detection is declared explicitly: since Angular 21 an app
    // without this provider is zoneless, and much of this UI relies on zones
    provideZoneChangeDetection(),
    provideRouter(routes, withInMemoryScrolling({ anchorScrolling: 'enabled', scrollPositionRestoration: 'top' })),
    provideApiBase(environment.apiUrl),
    // English and Filipino; a first visit follows the browser's language
    provideI18n(WEB_MESSAGES, { detect: true }),
    // Reports saved while offline are sent as soon as the app can reach the server (from any page)
    provideAppInitializer(() => {
      void inject(ReportQueueService).start();
    }),
    provideHttpClient(withXhr(), withInterceptors([apiCredentialsInterceptor, errorToastInterceptor])),
    // registerWhenStable:30000 registers the SW after the app is stable (or
    // after 30s, whichever comes first) so it never competes with initial
    // page load. isDevMode() guard means `ng serve` never installs it —
    // the SW is a production-build artifact only (see angular.json).
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000'
    })
  ]
};
