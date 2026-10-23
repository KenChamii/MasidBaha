import { Injectable, effect, signal } from '@angular/core';
import { SHARED_MESSAGES } from './shared-messages';

export type Lang = 'en' | 'fil';
export type Messages = Record<string, string>;
export type Dictionaries = Record<Lang, Messages>;
export type MessageParams = Record<string, string | number>;

export const LANGS: readonly Lang[] = ['en', 'fil'];

const STORAGE_KEY = 'masidbaha-lang';

// A message with {name} placeholders. A missing Filipino text falls back to English, and a missing key shows
// the key itself, so a gap is visible instead of blank.
export function translate(dictionaries: Dictionaries, lang: Lang, key: string, params?: MessageParams): string {
  const template = dictionaries[lang][key] ?? dictionaries.en[key] ?? key;
  if (!params) return template;
  return template.replace(/\{(\w+)\}/g, (placeholder, name: string) =>
    name in params ? String(params[name]) : placeholder
  );
}

// "3 minutes ago", in the chosen language
export function timeAgoText(translator: (key: string, params?: MessageParams) => string, isoDate: string): string {
  const minutes = Math.floor((Date.now() - new Date(isoDate).getTime()) / 60000);
  if (minutes < 1) return translator('time.now');
  if (minutes < 60) return translator('time.minutes', { n: minutes });
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return translator('time.hours', { n: hours });
  return translator('time.days', { n: Math.floor(hours / 24) });
}

function readStored(): Lang | null {
  try {
    const saved = localStorage.getItem(STORAGE_KEY);
    return saved === 'en' || saved === 'fil' ? saved : null;
  } catch {
    return null;
  }
}

// The language of the page. Each app adds its own texts with register(); the shared ones are built in.
@Injectable({ providedIn: 'root' })
export class I18nService {
  private readonly dictionaries: Dictionaries = { en: { ...SHARED_MESSAGES.en }, fil: { ...SHARED_MESSAGES.fil } };

  readonly lang = signal<Lang>(readStored() ?? 'en');

  constructor() {
    effect(() => {
      if (typeof document !== 'undefined') document.documentElement.lang = this.lang();
    });
  }

  register(messages: Dictionaries): void {
    for (const lang of LANGS) Object.assign(this.dictionaries[lang], messages[lang]);
  }

  // Reads the language signal, so a template or computed that calls it updates when the language changes
  t(key: string, params?: MessageParams): string {
    return translate(this.dictionaries, this.lang(), key, params);
  }

  timeAgo(isoDate: string): string {
    return timeAgoText((key, params) => this.t(key, params), isoDate);
  }

  set(lang: Lang): void {
    this.lang.set(lang);
    try {
      localStorage.setItem(STORAGE_KEY, lang);
    } catch {
      // storage can be blocked (private mode); the choice just won't persist
    }
  }

  toggle(): void {
    this.set(this.lang() === 'fil' ? 'en' : 'fil');
  }

  // First visit only: follow the browser's language. A saved choice always wins.
  detectFromBrowser(): void {
    if (readStored() || typeof navigator === 'undefined') return;
    const preferred = navigator.languages?.length ? navigator.languages : [navigator.language];
    if (preferred.some(tag => /^(fil|tl)\b/i.test(tag))) this.lang.set('fil');
  }
}
