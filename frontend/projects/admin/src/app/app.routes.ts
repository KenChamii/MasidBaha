import { Routes } from '@angular/router';
import { authGuard, guestGuard, roleGuard } from './core/auth.guard';

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
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        loadComponent: () =>
          import('./features/dashboard/dashboard-page.component').then(m => m.DashboardPageComponent),
        title: 'Dashboard · MasidBaha Admin'
      },
      {
        path: 'moderation',
        loadComponent: () =>
          import('./features/moderation/moderation-page.component').then(m => m.ModerationPageComponent),
        title: 'Moderation · MasidBaha Admin'
      },
      {
        path: 'reports',
        loadComponent: () => import('./features/reports/reports-page.component').then(m => m.ReportsPageComponent),
        title: 'Reports · MasidBaha Admin'
      },
      {
        path: 'reports/:id',
        loadComponent: () =>
          import('./features/report-detail/report-detail-page.component').then(m => m.ReportDetailPageComponent),
        title: 'Report · MasidBaha Admin'
      },
      {
        path: 'map',
        loadComponent: () => import('./features/map/admin-map-page.component').then(m => m.AdminMapPageComponent),
        title: 'Map · MasidBaha Admin'
      },
      {
        path: 'bans',
        loadComponent: () => import('./features/bans/bans-page.component').then(m => m.BansPageComponent),
        title: 'Bans · MasidBaha Admin'
      },
      {
        path: 'broadcast',
        loadComponent: () =>
          import('./features/broadcast/broadcast-page.component').then(m => m.BroadcastPageComponent),
        title: 'Broadcast · MasidBaha Admin'
      },
      {
        path: 'users',
        canActivate: [roleGuard('SuperAdmin')],
        loadComponent: () => import('./features/users/users-page.component').then(m => m.UsersPageComponent),
        title: 'Staff · MasidBaha Admin'
      },
      {
        path: 'audit',
        canActivate: [roleGuard('SuperAdmin')],
        loadComponent: () => import('./features/audit/audit-page.component').then(m => m.AuditPageComponent),
        title: 'Audit log · MasidBaha Admin'
      },
      {
        path: 'settings',
        canActivate: [roleGuard('SuperAdmin')],
        loadComponent: () => import('./features/settings/settings-page.component').then(m => m.SettingsPageComponent),
        title: 'Settings · MasidBaha Admin'
      },
      {
        path: 'account',
        loadComponent: () => import('./features/account/account-page.component').then(m => m.AccountPageComponent),
        title: 'Account · MasidBaha Admin'
      }
    ]
  },
  { path: '**', redirectTo: '' }
];
