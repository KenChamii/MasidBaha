import { Injectable } from '@angular/core';

const ADMIN_KEY_STORAGE = 'masidbaha_admin_key';

// Deliberately simple, mirroring the backend's simple API-key gate (see
// AdminAuthMiddleware.cs). This is not a login system — it's a place to
// remember whatever key the moderator typed in, sent as X-Admin-Key on
// every admin request. Swap for real auth if the app grows past a single
// trusted moderator.
@Injectable({ providedIn: 'root' })
export class AdminAuthService {
  get apiKey(): string | null {
    return localStorage.getItem(ADMIN_KEY_STORAGE);
  }

  setApiKey(key: string): void {
    localStorage.setItem(ADMIN_KEY_STORAGE, key);
  }

  clearApiKey(): void {
    localStorage.removeItem(ADMIN_KEY_STORAGE);
  }

  get hasApiKey(): boolean {
    return !!this.apiKey;
  }
}
