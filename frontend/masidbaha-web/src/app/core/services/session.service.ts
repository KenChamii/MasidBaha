import { Injectable } from '@angular/core';

const SESSION_KEY = 'masidbaha_session_id';

@Injectable({ providedIn: 'root' })
export class SessionService {
  readonly sessionId: string;

  constructor() {
    let existing = localStorage.getItem(SESSION_KEY);
    if (!existing) {
      existing = SessionService.generateId();
      localStorage.setItem(SESSION_KEY, existing);
    }
    this.sessionId = existing;
  }

  // crypto.randomUUID() only works in a secure context: HTTPS, or
  // localhost/127.0.0.1 specifically. A plain LAN or VPN IP over http does
  // not count, even when testing locally. This falls back to a simple
  // random id so the app still works when testing on a phone over LAN IP.
  // This id is only used to group reports from the same device, never for
  // anything security related, so the weaker randomness is fine here.
  private static generateId(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
      return crypto.randomUUID();
    }

    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
      const r = (Math.random() * 16) | 0;
      const v = c === 'x' ? r : (r & 0x3) | 0x8;
      return v.toString(16);
    });
  }
}
