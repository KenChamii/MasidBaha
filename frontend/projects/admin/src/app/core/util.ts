import { HttpErrorResponse } from '@angular/common/http';
import { Severity } from '@masidbaha/shared';
import { AdminReportStatus } from './api/admin.models';

// The most useful sentence in an API error: a validation message, the problem detail, or a fallback
export function problemMessage(error: unknown, fallback = 'Something went wrong. Please try again.'): string {
  if (!(error instanceof HttpErrorResponse)) return fallback;

  const validation = error.error?.errors as Record<string, string[]> | undefined;
  const first = validation ? Object.values(validation).flat()[0] : undefined;
  if (first) return first;

  if (error.status === 403) return 'Your role is not allowed to do this.';
  return error.error?.detail ?? fallback;
}

export const BADGE_BY_STATUS: Record<AdminReportStatus, string> = {
  1: 'badge--info',
  2: 'badge--ok',
  3: 'badge--muted'
};

export const SEVERITY_NAME: Record<Severity, string> = {
  1: 'Passable',
  2: 'Knee-deep',
  3: 'Waist-deep',
  4: 'Impassable'
};
export const SEVERITY_COLOR: Record<Severity, string> = { 1: '#34c6e8', 2: '#ffd166', 3: '#ff7a47', 4: '#e63946' };

export function locationOf(report: {
  city?: string;
  province?: string;
  region?: string;
  lat: number;
  lng: number;
}): string {
  return report.city || report.province || report.region || `${report.lat.toFixed(4)}, ${report.lng.toFixed(4)}`;
}

// Saves a fetched file through a temporary link (the CSV export needs the session cookie, so a plain <a href> won't do)
export function saveBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  link.click();
  URL.revokeObjectURL(url);
}
