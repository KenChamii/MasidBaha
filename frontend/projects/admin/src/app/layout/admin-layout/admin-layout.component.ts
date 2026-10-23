import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { BrandComponent, ThemeToggleComponent } from '@masidbaha/shared';
import { environment } from '../../../environments/environment';
import { AdminAuthService } from '../../core/api/admin-auth.service';

interface NavItem {
  path: string;
  label: string;
}

@Component({
  selector: 'app-admin-layout',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, BrandComponent, ThemeToggleComponent],
  templateUrl: './admin-layout.component.html',
  styleUrl: './admin-layout.component.scss'
})
export class AdminLayoutComponent {
  private readonly router = inject(Router);

  protected readonly auth = inject(AdminAuthService);
  protected readonly publicSiteUrl = environment.publicSiteUrl.replace(/\/$/, '');

  // Everyone reads; only SuperAdmin sees staff, audit and settings
  protected readonly navItems = computed<NavItem[]>(() => {
    const items: NavItem[] = [
      { path: '/dashboard', label: 'Dashboard' },
      { path: '/moderation', label: 'Moderation' },
      { path: '/reports', label: 'Reports' },
      { path: '/map', label: 'Map' },
      { path: '/bans', label: 'Bans' },
      { path: '/broadcast', label: 'Broadcast' }
    ];

    if (this.auth.isSuperAdmin()) {
      items.push(
        { path: '/users', label: 'Staff' },
        { path: '/audit', label: 'Audit log' },
        { path: '/settings', label: 'Settings' }
      );
    }
    return items;
  });

  protected signOut(): void {
    this.auth.logout().subscribe(() => void this.router.navigate(['/login']));
  }
}
