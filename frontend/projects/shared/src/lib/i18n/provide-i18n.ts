import { EnvironmentProviders, inject, provideAppInitializer } from '@angular/core';
import { Dictionaries, I18nService } from './i18n.service';

// Adds an app's texts before anything is drawn. detect: follow the browser's language on a first visit.
export function provideI18n(messages: Dictionaries, options: { detect?: boolean } = {}): EnvironmentProviders {
  return provideAppInitializer(() => {
    const i18n = inject(I18nService);
    i18n.register(messages);
    if (options.detect) i18n.detectFromBrowser();
  });
}
