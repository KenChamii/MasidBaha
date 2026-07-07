import { Injectable } from '@angular/core';

const SESSION_KEY = 'masidbaha_session_id';

@Injectable({ providedIn: 'root' })
export class SessionService {
  readonly sessionId: string;

  constructor() {
    let existing = localStorage.getItem(SESSION_KEY);
    if (!existing) {
      existing = crypto.randomUUID();
      localStorage.setItem(SESSION_KEY, existing);
    }
    this.sessionId = existing;
  }
}