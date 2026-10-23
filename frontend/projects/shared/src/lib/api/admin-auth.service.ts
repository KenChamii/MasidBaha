import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, of, tap } from 'rxjs';
import { API_BASE_URL } from './api-config';

export type AdminRole = 'SuperAdmin' | 'Moderator' | 'Viewer';

export interface AdminUser {
  id: string;
  email: string;
  displayName: string;
  roles: AdminRole[];
}

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

  login(email: string, password: string): Observable<AdminUser> {
    return this.http.post<AdminUser>(`${this.baseUrl}/login`, { email, password }).pipe(
      tap(user => {
        this.restored = true;
        this.user.set(user);
      })
    );
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

  // Called when the API says the session is gone (401), without another round trip
  clearLocalSession(): void {
    this.restored = true;
    this.user.set(null);
  }

  hasAnyRole(...roles: AdminRole[]): boolean {
    const current = this.user();
    return !!current && current.roles.some(role => roles.includes(role));
  }
}
