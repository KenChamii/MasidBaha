import * as L from 'leaflet';
import { PULSE_BY_SEVERITY, SEVERITY_META, Severity } from '@masidbaha/shared';
import { Translate, english } from '../../core/i18n/english';

export function buildFloodIcon(severity: Severity, hasPhoto: boolean): L.DivIcon {
  const meta = SEVERITY_META[severity];
  const pulse = PULSE_BY_SEVERITY[severity];
  const photoBadge = hasPhoto ? '<span class="flood-marker__photo-badge">📷</span>' : '';

  return L.divIcon({
    className: 'flood-marker',
    html: `
      <div class="flood-marker__badge" style="background:${meta.color}; --pulse-duration:${pulse.duration}s; --pulse-scale:${pulse.scale};">
        <svg class="flood-marker__icon" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
          <path d="M3 9.5c1.6 1.4 3.2 1.4 4.8 0s3.2-1.4 4.8 0 3.2 1.4 4.8 0 3.2-1.4 4.8 0" stroke="#0B1F33" stroke-width="2" stroke-linecap="round"/>
          <path d="M3 15c1.6 1.4 3.2 1.4 4.8 0s3.2-1.4 4.8 0 3.2 1.4 4.8 0 3.2-1.4 4.8 0" stroke="#0B1F33" stroke-width="2" stroke-linecap="round" opacity="0.55"/>
        </svg>
        ${photoBadge}
      </div>
    `,
    iconSize: [34, 34],
    iconAnchor: [17, 17],
    popupAnchor: [0, -17]
  });
}

// The words come from the message list and the number is a number, so nothing user-written reaches the HTML
export function buildPopupHtml(confidenceScore: number, hasPhoto: boolean, t: Translate = english): string {
  const photoButton = hasPhoto
    ? `<button type="button" class="flood-popup__photo-btn" data-action="view-photo">${t('popup.viewPhoto')}</button>`
    : '';

  return `
    <div class="flood-popup">
      <p class="flood-popup__confidence">${t('popup.confidence', { n: confidenceScore })}</p>
      ${photoButton}
      <div class="flood-popup__actions">
        <button type="button" data-action="confirm">${t('popup.confirm')}</button>
        <button type="button" data-action="deny">${t('popup.deny')}</button>
      </div>
      <button type="button" class="flood-popup__share-btn" data-action="share">${t('popup.share')}</button>
    </div>
  `;
}
