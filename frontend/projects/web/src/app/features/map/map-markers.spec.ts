import { SEVERITY_META, translate } from '@masidbaha/shared';
import { WEB_MESSAGES } from '../../core/i18n/messages';
import { buildFloodIcon, buildPopupHtml } from './map-markers';

describe('map markers', () => {
  describe('buildFloodIcon', () => {
    it.each([1, 2, 3, 4] as const)('colours severity %d with its own colour', severity => {
      const icon = buildFloodIcon(severity, false);

      expect(String(icon.options.html)).toContain(SEVERITY_META[severity].color);
    });

    it('pulses faster for worse floods', () => {
      const mild = String(buildFloodIcon(1, false).options.html);
      const severe = String(buildFloodIcon(4, false).options.html);

      expect(mild).toContain('--pulse-duration:3.2s');
      expect(severe).toContain('--pulse-duration:1.4s');
    });

    it('marks a report that is still waiting to be sent', () => {
      const pending = buildFloodIcon(2, false, true);

      expect(pending.options.className).toContain('flood-marker--pending');
      expect(String(pending.options.html)).toContain('⏳');
      expect(buildFloodIcon(2, false).options.className).not.toContain('pending');
    });

    it('adds the camera badge only when there is a photo', () => {
      expect(String(buildFloodIcon(2, true).options.html)).toContain('flood-marker__photo-badge');
      expect(String(buildFloodIcon(2, false).options.html)).not.toContain('flood-marker__photo-badge');
    });
  });

  describe('buildPopupHtml', () => {
    it('shows the confidence score and the vote buttons', () => {
      const html = buildPopupHtml(7, false);

      expect(html).toContain('Confidence: 7');
      expect(html).toContain('data-action="confirm"');
      expect(html).toContain('data-action="deny"');
    });

    it('speaks Filipino when asked to', () => {
      const html = buildPopupHtml(7, true, (key, params) => translate(WEB_MESSAGES, 'fil', key, params));

      expect(html).toContain('Tiwala: 7');
      expect(html).toContain('Kumpirmahin');
      expect(html).toContain('Tingnan ang larawan');
    });

    it('lets people share the report', () => {
      expect(buildPopupHtml(1, false)).toContain('data-action="share"');
    });

    it('offers the photo only when the report has one', () => {
      expect(buildPopupHtml(1, true)).toContain('data-action="view-photo"');
      expect(buildPopupHtml(1, false)).not.toContain('data-action="view-photo"');
    });

    it('is built from numbers only, so nothing user-written can reach the HTML', () => {
      expect(buildPopupHtml(3, true)).not.toMatch(/<script/i);
    });
  });
});
