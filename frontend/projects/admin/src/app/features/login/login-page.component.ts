import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AdminAuthService, BrandComponent, ThemeToggleComponent } from '@masidbaha/shared';

@Component({
  selector: 'app-login-page',
  imports: [FormsModule, BrandComponent, ThemeToggleComponent],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.scss'
})
export class LoginPageComponent {
  private readonly auth = inject(AdminAuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly isSigningIn = signal(false);
  protected readonly error = signal<string | null>(null);

  protected signIn(): void {
    if (!this.email().trim() || !this.password() || this.isSigningIn()) return;

    this.isSigningIn.set(true);
    this.error.set(null);

    this.auth.login(this.email().trim(), this.password()).subscribe({
      next: () => {
        this.isSigningIn.set(false);
        this.password.set('');
        void this.router.navigateByUrl(this.returnUrl());
      },
      error: (err: HttpErrorResponse) => {
        this.isSigningIn.set(false);
        // The API's problem details carry a safe, human-readable message
        this.error.set(
          err.error?.detail ??
            (err.status === 429 ? 'Too many attempts. Please wait a moment.' : 'Could not sign in. Please try again.')
        );
      }
    });
  }

  // Only in-app paths are honoured, so a crafted link can't redirect off-site
  private returnUrl(): string {
    const requested = this.route.snapshot.queryParamMap.get('returnUrl');
    return requested && requested.startsWith('/') && !requested.startsWith('//') ? requested : '/';
  }
}
