import { TestBed } from '@angular/core/testing';
import { Dictionaries, I18nService, timeAgoText, translate } from './i18n.service';
import { SHARED_MESSAGES } from './shared-messages';

const dictionaries: Dictionaries = {
  en: { hello: 'Hello {name}', onlyEnglish: 'Only English' },
  fil: { hello: 'Kumusta {name}' }
};

describe('translate', () => {
  it('fills in {placeholders}', () => {
    expect(translate(dictionaries, 'en', 'hello', { name: 'Ana' })).toBe('Hello Ana');
  });

  it('uses the chosen language', () => {
    expect(translate(dictionaries, 'fil', 'hello', { name: 'Ana' })).toBe('Kumusta Ana');
  });

  it('falls back to English when the other language has no text yet', () => {
    expect(translate(dictionaries, 'fil', 'onlyEnglish')).toBe('Only English');
  });

  it('shows the key itself when nothing has it, so a gap is visible', () => {
    expect(translate(dictionaries, 'en', 'missing.key')).toBe('missing.key');
  });

  it('leaves an unknown placeholder alone', () => {
    expect(translate(dictionaries, 'en', 'hello', { other: 'x' })).toBe('Hello {name}');
  });
});

describe('SHARED_MESSAGES', () => {
  it('has every text in both languages', () => {
    expect(Object.keys(SHARED_MESSAGES.fil).sort()).toEqual(Object.keys(SHARED_MESSAGES.en).sort());
  });
});

describe('I18nService', () => {
  let service: I18nService;
  const original = Object.getOwnPropertyDescriptor(window.navigator, 'languages');

  const setBrowserLanguages = (languages: string[]) =>
    Object.defineProperty(window.navigator, 'languages', { value: languages, configurable: true });

  beforeEach(() => {
    localStorage.clear();
    service = TestBed.inject(I18nService);
    service.register(dictionaries);
  });

  afterEach(() => {
    localStorage.clear();
    if (original) Object.defineProperty(window.navigator, 'languages', original);
  });

  it('starts in English', () => {
    expect(service.lang()).toBe('en');
    expect(service.t('hello', { name: 'Ana' })).toBe('Hello Ana');
  });

  it('switches language, remembers it, and sets the page language', () => {
    service.set('fil');

    expect(service.t('hello', { name: 'Ana' })).toBe('Kumusta Ana');
    expect(localStorage.getItem('masidbaha-lang')).toBe('fil');
    TestBed.tick();
    expect(document.documentElement.lang).toBe('fil');
  });

  it('toggles back and forth', () => {
    service.toggle();
    expect(service.lang()).toBe('fil');
    service.toggle();
    expect(service.lang()).toBe('en');
  });

  it('follows a Filipino browser on a first visit', () => {
    setBrowserLanguages(['fil-PH', 'en']);
    service.detectFromBrowser();
    expect(service.lang()).toBe('fil');
  });

  it('also recognises the older "tl" code', () => {
    setBrowserLanguages(['tl']);
    service.detectFromBrowser();
    expect(service.lang()).toBe('fil');
  });

  it('stays English for other browsers', () => {
    setBrowserLanguages(['en-PH', 'ceb']);
    service.detectFromBrowser();
    expect(service.lang()).toBe('en');
  });

  it('never overrides a saved choice', () => {
    localStorage.setItem('masidbaha-lang', 'en');
    setBrowserLanguages(['fil-PH']);
    service.detectFromBrowser();
    expect(service.lang()).toBe('en');
  });
});

describe('timeAgoText', () => {
  const t = (key: string, params?: Record<string, string | number>) =>
    translate({ en: { ...SHARED_MESSAGES.en }, fil: { ...SHARED_MESSAGES.fil } }, 'en', key, params);

  const ago = (ms: number) => new Date(Date.now() - ms).toISOString();

  it('says "just now" within a minute', () => {
    expect(timeAgoText(t, ago(10_000))).toBe('just now');
  });

  it('counts minutes, hours and days', () => {
    expect(timeAgoText(t, ago(5 * 60_000))).toBe('5m ago');
    expect(timeAgoText(t, ago(3 * 3_600_000))).toBe('3h ago');
    expect(timeAgoText(t, ago(2 * 86_400_000))).toBe('2d ago');
  });
});
