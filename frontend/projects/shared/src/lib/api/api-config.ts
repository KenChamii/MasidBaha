import { EnvironmentProviders, InjectionToken, makeEnvironmentProviders } from '@angular/core';

// Where the API lives. Each app provides its own value (from its environment
// file), so the library never imports an app's configuration.
// An empty string means "same origin" (the API is served behind the same host).
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL');

export function provideApiBase(apiUrl: string): EnvironmentProviders {
  return makeEnvironmentProviders([{ provide: API_BASE_URL, useValue: apiUrl }]);
}
