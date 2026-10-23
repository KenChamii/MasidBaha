import { translate } from '@masidbaha/shared';
import { english } from './english';
import { WEB_MESSAGES } from './messages';

describe('WEB_MESSAGES', () => {
  it('has every text in both languages', () => {
    const en = Object.keys(WEB_MESSAGES.en).sort();
    const fil = Object.keys(WEB_MESSAGES.fil).sort();

    expect(fil.filter(key => !en.includes(key))).toEqual([]);
    expect(en.filter(key => !fil.includes(key))).toEqual([]);
  });

  it('uses the same {placeholders} in both languages, so a translation cannot lose a number', () => {
    const placeholders = (text: string) => [...text.matchAll(/\{(\w+)\}/g)].map(match => match[1]).sort();

    for (const key of Object.keys(WEB_MESSAGES.en)) {
      expect(placeholders(WEB_MESSAGES.fil[key]), key).toEqual(placeholders(WEB_MESSAGES.en[key]));
    }
  });

  it('has no empty text', () => {
    for (const lang of ['en', 'fil'] as const) {
      for (const [key, text] of Object.entries(WEB_MESSAGES[lang])) expect(text.trim(), `${lang}:${key}`).not.toBe('');
    }
  });

  it('has the four severity names the report form and the list use', () => {
    for (const severity of [1, 2, 3, 4]) {
      expect(WEB_MESSAGES.en[`severity.${severity}`]).toBeTruthy();
      expect(WEB_MESSAGES.fil[`severity.${severity}`]).toBeTruthy();
    }
  });
});

describe('english', () => {
  it('is the English translator for code outside Angular', () => {
    expect(english('popup.confidence', { n: 4 })).toBe('Confidence: 4');
  });

  it('matches translate() with the English list', () => {
    expect(english('map.report')).toBe(translate(WEB_MESSAGES, 'en', 'map.report'));
  });
});
