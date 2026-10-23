import { HttpClient, HttpResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { API_BASE_URL } from '@masidbaha/shared';
import { Observable, catchError, map, of, tap } from 'rxjs';
import { AdminRole, AdminUser, TwoFactorSetup } from './admin.models';

// What a sign-in attempt can lead to: signed in, or the password was right and a code is still needed
export type LoginOutcome = 'signed-in' | 'two-factor';

// Back-office sign-in. The session itself is an HttpOnly cookie set by the
// API, so nothing secret is ever stored in the browser; this service only
// remembers who is signed in, to show the right screen and buttons.
@Injectable({ providedIn: 'root' })
export class AdminAuthService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${inject(API_BASE_URL)}/api/v1/auth`;

  // Whether we've asked the API who is signed in since the page loaded
  private restored = false;

  readonly user = signal<AdminUser | null>(null);
  readonly isSignedIn = computed(() => this.user() !== null);

  // Moderators and super admins may act on reports; viewers only read
  readonly canModerate = computed(() => this.hasAnyRole('Moderator', 'SuperAdmin'));
  readonly isSuperAdmin = computed(() => this.hasAnyRole('SuperAdmin'));

  // A temporary password only opens the change-password screen
  readonly mustChangePassword = computed(() => this.user()?.mustChangePassword ?? false);

  // Asks the API once per page load (the cookie survives a reload, the page state doesn't)
  restore(): Observable<AdminUser | null> {
    return this.http.get<AdminUser>(`${this.baseUrl}/me`).pipe(
      catchError(() => of(null)),
      tap(user => {
        this.restored = true;
        this.user.set(user);
      })
    );
  }

  // For route guards: reuse what we know instead of calling /me on every navigation
  ensureRestored(): Observable<AdminUser | null> {
    return this.restored ? of(this.user()) : this.restore();
  }

  // 200 = signed in; 202 = right password, but a two-factor code is still needed
  login(email: string, password: string): Observable<LoginOutcome> {
    return this.http
      .post<AdminUser | { requiresTwoFactor: true }>(
        `${this.baseUrl}/login`,
        { email, password },
        { observe: 'response' }
      )
      .pipe(map(response => this.finishLogin(response)));
  }

  loginWithCode(code: string): Observable<LoginOutcome> {
    return this.http
      .post<AdminUser>(`${this.baseUrl}/login/2fa`, { code }, { observe: 'response' })
      .pipe(map(response => this.finishLogin(response)));
  }

  loginWithRecoveryCode(recoveryCode: string): Observable<LoginOutcome> {
    return this.http
      .post<AdminUser>(`${this.baseUrl}/login/2fa`, { recoveryCode }, { observe: 'response' })
      .pipe(map(response => this.finishLogin(response)));
  }

  logout(): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/logout`, {}).pipe(
      tap(() => this.user.set(null)),
      catchError(() => {
        this.user.set(null);
        return of(undefined);
      })
    );
  }

  changePassword(currentPassword: string, newPassword: string): Observable<AdminUser> {
    return this.http
      .post<AdminUser>(`${this.baseUrl}/change-password`, { currentPassword, newPassword })
      .pipe(tap(user => this.user.set(user)));
  }

  setupTwoFactor(): Observable<TwoFactorSetup> {
    return this.http.post<TwoFactorSetup>(`${this.baseUrl}/2fa/setup`, {});
  }

  // Returns the recovery codes (shown once) and refreshes who is signed in
  enableTwoFactor(code: string): Observable<string[]> {
    return this.http.post<{ recoveryCodes: string[] }>(`${this.baseUrl}/2fa/enable`, { code }).pipe(
      tap(() => this.user.update(user => (user ? { ...user, twoFactorEnabled: true } : user))),
      map(result => result.recoveryCodes)
    );
  }

  disableTwoFactor(password: string, code: string): Observable<AdminUser> {
    return this.http
      .post<AdminUser>(`${this.baseUrl}/2fa/disable`, { password, code })
      .pipe(tap(user => this.user.set(user)));
  }

  // Called when the API says the session is gone (401), without another round trip
  clearLocalSession(): void {
    this.restored = true;
    this.user.set(null);
  }

  hasAnyRole(...roles: AdminRole[]): boolean {
    const current = this.user();
    return !!current && current.roles.some(role => roles.includes(role));
  }

  private finishLogin(response: HttpResponse<AdminUser | { requiresTwoFactor: true }>): LoginOutcome {
    if (response.status === 202) return 'two-factor';

    this.restored = true;
    this.user.set(response.body as AdminUser);
    return 'signed-in';
  }
}
