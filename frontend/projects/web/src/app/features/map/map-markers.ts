import * as L from 'leaflet';
import 'leaflet.markercluster';
import { PULSE_BY_SEVERITY, SEVERITY_META, Severity } from '@masidbaha/shared';
import { Translate, english } from '../../core/i18n/english';

// The plugin adds markerClusterGroup to the global L, not to the module namespace that
// `import * as L` gives under esbuild (the same reason analytics reads heatLayer from window.L)
export function createClusterGroup(options: L.MarkerClusterGroupOptions): L.MarkerClusterGroup {
  return (window as unknown as { L: typeof L }).L.markerClusterGroup(options);
}

// A marker that remembers how bad its flood is, so a cluster can show the worst one inside it
export interface FloodMarkerOptions extends L.MarkerOptions {
  severity: Severity;
}

export function floodMarker(latLng: L.LatLngExpression, severity: Severity, hasPhoto: boolean): L.Marker {
  return L.marker(latLng, { icon: buildFloodIcon(severity, hasPhoto), severity } as FloodMarkerOptions);
}

// The worst severity among a group of markers (they all carry one; 1 is the mildest)
export function worstSeverity(markers: L.Marker[]): Severity {
  return markers.reduce<Severity>(
    (worst, marker) => Math.max(worst, (marker.options as Partial<FloodMarkerOptions>).severity ?? 1) as Severity,
    1
  );
}

// Several reports close together: one badge with the count. Bigger groups get a bigger badge.
export function buildClusterIcon(count: number, severity: Severity): L.DivIcon {
  const size = count < 10 ? 38 : count < 50 ? 46 : 54;
  const label = count > 99 ? '99+' : String(count);

  return L.divIcon({
    className: 'flood-cluster',
    html: `<div class="flood-cluster__badge" style="background:${SEVERITY_META[severity].color}"><span>${label}</span></div>`,
    iconSize: [size, size]
  });
}

// pending: a report saved on this device that has not reached the server yet
export function buildFloodIcon(severity: Severity, hasPhoto: boolean, pending = false): L.DivIcon {
  const meta = SEVERITY_META[severity];
  const pulse = PULSE_BY_SEVERITY[severity];
  const badge = pending
    ? '<span class="flood-marker__photo-badge">⏳</span>'
    : hasPhoto
      ? '<span class="flood-marker__photo-badge">📷</span>'
      : '';

  return L.divIcon({
    className: pending ? 'flood-marker flood-marker--pending' : 'flood-marker',
    html: `
      <div class="flood-marker__badge" style="background:${meta.color}; --pulse-duration:${pulse.duration}s; --pulse-scale:${pulse.scale};">
        <svg class="flood-marker__icon" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
          <path d="M3 9.5c1.6 1.4 3.2 1.4 4.8 0s3.2-1.4 4.8 0 3.2 1.4 4.8 0 3.2-1.4 4.8 0" stroke="#0B1F33" stroke-width="2" stroke-linecap="round"/>
          <path d="M3 15c1.6 1.4 3.2 1.4 4.8 0s3.2-1.4 4.8 0 3.2 1.4 4.8 0 3.2-1.4 4.8 0" stroke="#0B1F33" stroke-width="2" stroke-linecap="round" opacity="0.55"/>
        </svg>
        ${badge}
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

// The popup of a report that is still waiting to be sent
export function buildPendingPopupHtml(t: Translate = english): string {
  return `
    <div class="flood-popup">
      <p class="flood-popup__confidence"><strong>${t('queue.popupTitle')}</strong></p>
      <p class="flood-popup__pending">${t('queue.popupBody')}</p>
    </div>
  `;
}
