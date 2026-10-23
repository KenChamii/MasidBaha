import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./shared/layouts/public-layout/public-layout.component').then(m => m.PublicLayoutComponent),
    children: [
      {
        path: '',
        loadComponent: () => import('./features/landing/landing.component').then(m => m.LandingComponent),
        title: 'MasidBaha — Real-time flood map'
      }
    ]
  },
  {
    path: 'map',
    loadComponent: () => import('./features/map/map-page.component').then(m => m.MapPageComponent),
    title: 'MasidBaha — Mapa'
  },
  {
    path: 'analytics',
    loadComponent: () => import('./features/analytics/analytics-page.component').then(m => m.AnalyticsPageComponent),
    title: 'MasidBaha — Analytics'
  },
  { path: '**', redirectTo: '' }
];
