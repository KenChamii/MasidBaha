import { TestBed } from '@angular/core/testing';
import { ThemeService } from './theme.service';

function stubMatchMedia(prefersDark: boolean): void {
  vi.stubGlobal('matchMedia', (query: string) => ({ matches: prefersDark && query.includes('dark') }));
}

describe('ThemeService', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
    document.head.innerHTML = '<meta name="theme-color" content="#000">';
    TestBed.resetTestingModule();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('follows the OS preference when nothing is saved', () => {
    stubMatchMedia(true);

    const service = TestBed.inject(ThemeService);

    expect(service.theme()).toBe('dark');
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
  });

  it('defaults to light when matchMedia is not available', () => {
    vi.stubGlobal('matchMedia', undefined);

    expect(TestBed.inject(ThemeService).theme()).toBe('light');
  });

  it('prefers a saved choice over the OS setting', () => {
    stubMatchMedia(true);
    localStorage.setItem('masidbaha-theme', 'light');

    expect(TestBed.inject(ThemeService).theme()).toBe('light');
  });

  it('ignores a corrupt saved value', () => {
    stubMatchMedia(false);
    localStorage.setItem('masidbaha-theme', 'purple');

    expect(TestBed.inject(ThemeService).theme()).toBe('light');
  });

  it('toggles, applies it to the page and remembers it', () => {
    stubMatchMedia(false);
    const service = TestBed.inject(ThemeService);

    service.toggle();

    expect(service.theme()).toBe('dark');
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(document.querySelector('meta[name="theme-color"]')?.getAttribute('content')).toBe('#08131F');
    expect(localStorage.getItem('masidbaha-theme')).toBe('dark');

    service.toggle();
    expect(service.theme()).toBe('light');
    expect(localStorage.getItem('masidbaha-theme')).toBe('light');
  });

  it('still switches when storage is blocked', () => {
    stubMatchMedia(false);
    const service = TestBed.inject(ThemeService);
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });

    expect(() => service.toggle()).not.toThrow();
    expect(service.theme()).toBe('dark');
  });
});
