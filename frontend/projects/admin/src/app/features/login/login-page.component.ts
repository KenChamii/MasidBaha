import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { BrandComponent, ThemeToggleComponent } from '@masidbaha/shared';
import { Observable } from 'rxjs';
import { AdminAuthService, LoginOutcome } from '../../core/api/admin-auth.service';

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
  protected readonly code = signal('');
  protected readonly isSigningIn = signal(false);
  protected readonly error = signal<string | null>(null);

  // After the password: the code from the authenticator app (or a one-time recovery code)
  protected readonly needsCode = signal(false);
  protected readonly useRecoveryCode = signal(false);

  protected signIn(): void {
    if (this.needsCode()) {
      this.submitCode();
      return;
    }

    if (!this.email().trim() || !this.password()) return;
    this.run(this.auth.login(this.email().trim(), this.password()));
  }

  protected toggleRecovery(): void {
    this.useRecoveryCode.update(value => !value);
    this.code.set('');
    this.error.set(null);
  }

  protected startOver(): void {
    this.needsCode.set(false);
    this.useRecoveryCode.set(false);
    this.code.set('');
    this.password.set('');
    this.error.set(null);
  }

  private submitCode(): void {
    const code = this.code().trim();
    if (!code) return;

    this.run(this.useRecoveryCode() ? this.auth.loginWithRecoveryCode(code) : this.auth.loginWithCode(code));
  }

  private run(attempt: Observable<LoginOutcome>): void {
    if (this.isSigningIn()) return;

    this.isSigningIn.set(true);
    this.error.set(null);

    attempt.subscribe({
      next: outcome => {
        this.isSigningIn.set(false);
        this.password.set('');
        this.code.set('');

        if (outcome === 'two-factor') {
          this.needsCode.set(true);
          return;
        }
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
