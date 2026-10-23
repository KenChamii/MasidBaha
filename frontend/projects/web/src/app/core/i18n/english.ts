import { translate } from '@masidbaha/shared';
import { WEB_MESSAGES } from './messages';

export type Translate = (key: string, params?: Record<string, string | number>) => string;

// The default for code that draws on the map: English, unless the caller passes the live translator
export const english: Translate = (key, params) => translate(WEB_MESSAGES, 'en', key, params);
