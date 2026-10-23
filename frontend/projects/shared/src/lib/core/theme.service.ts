import { Injectable, signal } from '@angular/core';

export type Theme = 'light' | 'dark';

// Shared by every app, so the choice carries over between the public site and the admin
const STORAGE_KEY = 'masidbaha-theme';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly theme = signal<Theme>(this.readInitialTheme());

  constructor() {
    this.apply(this.theme());
  }

  toggle(): void {
    const next: Theme = this.theme() === 'dark' ? 'light' : 'dark';
    this.theme.set(next);
    this.apply(next);
    try {
      localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // storage can be blocked (private mode); the choice just won't persist
    }
  }

  // Saved choice wins, otherwise follow the OS preference
  private readInitialTheme(): Theme {
    try {
      const saved = localStorage.getItem(STORAGE_KEY);
      if (saved === 'light' || saved === 'dark') return saved;
    } catch {
      /* fall through to OS preference */
    }

    // matchMedia is missing outside a real browser (tests, server rendering)
    const prefersDark =
      typeof window !== 'undefined' &&
      typeof window.matchMedia === 'function' &&
      window.matchMedia('(prefers-color-scheme: dark)').matches;

    return prefersDark ? 'dark' : 'light';
  }

  private apply(theme: Theme): void {
    document.documentElement.setAttribute('data-theme', theme);
    document
      .querySelector('meta[name="theme-color"]')
      ?.setAttribute('content', theme === 'dark' ? '#08131F' : '#0B1F33');
  }
}
