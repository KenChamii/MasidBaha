import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./features/landing/landing.component').then(m => m.LandingComponent), title: 'MasidBaha — Real-time flood map' },
  { path: 'map', loadComponent: () => import('./features/map/map-page.component').then(m => m.MapPageComponent), title: 'MasidBaha — Mapa' },
  { path: '**', redirectTo: '' }
];