import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AdminAuthService, BrandComponent, ThemeToggleComponent } from '@masidbaha/shared';
import { environment } from '../../../environments/environment';

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

  protected signOut(): void {
    this.auth.logout().subscribe(() => void this.router.navigate(['/login']));
  }
}
