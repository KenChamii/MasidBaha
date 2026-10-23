import { Injectable, computed, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, of, tap } from 'rxjs';
import { environment } from '../../../environments/environment';

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
  private readonly baseUrl = `${environment.apiUrl}/api/v1/auth`;

  readonly user = signal<AdminUser | null>(null);
  readonly isSignedIn = computed(() => this.user() !== null);

  // Moderators and super admins may act on reports; viewers only read
  readonly canModerate = computed(() => this.hasAnyRole('Moderator', 'SuperAdmin'));

  constructor(private http: HttpClient) {}

  // Restores the session after a page reload (the cookie survives, the page state doesn't)
  restore(): Observable<AdminUser | null> {
    return this.http.get<AdminUser>(`${this.baseUrl}/me`).pipe(
      catchError(() => of(null)),
      tap(user => this.user.set(user))
    );
  }

  login(email: string, password: string): Observable<AdminUser> {
    return this.http.post<AdminUser>(`${this.baseUrl}/login`, { email, password }).pipe(
      tap(user => this.user.set(user))
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
    this.user.set(null);
  }

  private hasAnyRole(...roles: AdminRole[]): boolean {
    const current = this.user();
    return !!current && current.roles.some(role => roles.includes(role));
  }
}
