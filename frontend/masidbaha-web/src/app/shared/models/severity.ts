import { Severity } from './flood-report.model';

export const SEVERITY_META: Record<Severity, { label: string; color: string }> = {
  1: { label: 'Madadaanan', color: '#34C6E8' },
  2: { label: 'Tuhod ang lalim', color: '#FFD166' },
  3: { label: 'Baywang ang lalim', color: '#FF7A47' },
  4: { label: 'Hindi madadaanan', color: '#E63946' }
};

// Every marker pulses; higher severity pulses faster and wider so the
// visual hierarchy between severities still reads instantly on the map.
export const PULSE_BY_SEVERITY: Record<Severity, { duration: number; scale: number }> = {
  1: { duration: 3.2, scale: 1.35 },
  2: { duration: 2.6, scale: 1.45 },
  3: { duration: 2.0, scale: 1.6 },
  4: { duration: 1.4, scale: 1.8 }
};

export const SEVERITY_OPTIONS: { value: Severity; label: string }[] =
  (Object.keys(SEVERITY_META).map(Number) as Severity[])
    .map(value => ({ value, label: SEVERITY_META[value].label }));

export const severityLabel = (severity: Severity): string => SEVERITY_META[severity].label;
export const severityColor = (severity: Severity): string => SEVERITY_META[severity].color;
