import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/login/login-page.component').then(m => m.LoginPageComponent),
    title: 'Sign in · MasidBaha Admin'
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/admin-layout/admin-layout.component').then(m => m.AdminLayoutComponent),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'reports' },
      {
        path: 'reports',
        loadComponent: () => import('./features/reports/reports-page.component').then(m => m.ReportsPageComponent),
        title: 'Reports · MasidBaha Admin'
      }
    ]
  },
  { path: '**', redirectTo: '' }
];
