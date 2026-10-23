import { DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AdminApiService } from '../../core/api/admin-api.service';
import { Dashboard } from '../../core/api/admin.models';
import { SEVERITY_COLOR, SEVERITY_NAME } from '../../core/util';
import { BarItem, BarListComponent } from '../../ui/bar-list.component';
import { ChartPoint, LineChartComponent } from '../../ui/line-chart.component';

interface Kpi {
  label: string;
  value: string;
  // Change against the period before it (reports only)
  delta?: string;
  link?: string;
}

// Plain-words change against the previous period; "no change" rather than a meaningless 0%
export function describeChange(now: number, before: number, period: string): string {
  if (before === 0) return now === 0 ? `no reports in the ${period} before` : `new: none in the ${period} before`;
  const percent = Math.round(((now - before) / before) * 100);
  if (percent === 0) return `no change from the ${period} before`;
  return `${percent > 0 ? '+' : ''}${percent}% vs the ${period} before`;
}

@Component({
  selector: 'app-dashboard-page',
  imports: [RouterLink, DatePipe, LineChartComponent, BarListComponent],
  templateUrl: './dashboard-page.component.html',
  styles: `
    .kpi {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(190px, 1fr));
      gap: 1rem;
      margin-bottom: 1rem;
    }
    .kpi__value {
      font-family: var(--mb-font-display);
      font-size: 2rem;
      font-weight: 700;
      margin: 0;
    }
    .kpi__value a {
      color: var(--mb-water-text);
    }
    .kpi__label {
      margin: 0;
      font-family: var(--mb-font-mono);
      font-size: 0.7rem;
      letter-spacing: 0.1em;
      text-transform: uppercase;
      color: var(--mb-muted);
    }
    .kpi__delta {
      margin: 2px 0 0;
      font-size: 0.78rem;
      color: var(--mb-muted);
    }
  `
})
export class DashboardPageComponent implements OnInit {
  private readonly api = inject(AdminApiService);

  protected readonly data = signal<Dashboard | null>(null);
  protected readonly failed = signal(false);

  protected readonly kpis = computed<Kpi[]>(() => {
    const d = this.data();
    if (!d) return [];
    return [
      { label: 'Active reports', value: String(d.activeReports) },
      {
        label: 'Reports, last 24h',
        value: String(d.reportsLast24h),
        delta: describeChange(d.reportsLast24h, d.reportsPrevious24h, '24h')
      },
      {
        label: 'Reports, last 7 days',
        value: String(d.reportsLast7d),
        delta: describeChange(d.reportsLast7d, d.reportsPrevious7d, '7 days')
      },
      { label: 'Votes, last 24h', value: String(d.votesLast24h) },
      { label: 'Needs review', value: String(d.flaggedReports), link: '/moderation' },
      { label: 'Alert subscribers', value: String(d.pushSubscribers) },
      {
        label: 'Resolved share, 30d',
        value: d.resolvedShare30d === null ? '—' : `${d.resolvedShare30d}%`,
        delta: 'of reports that closed'
      }
    ];
  });

  protected readonly daily = computed<ChartPoint[]>(() =>
    (this.data()?.dailyReports30d ?? []).map(d => ({
      label: new Date(d.day + 'T00:00:00').toLocaleDateString(undefined, { month: 'short', day: 'numeric' }),
      value: d.count
    }))
  );

  protected readonly severities = computed<BarItem[]>(() =>
    (this.data()?.severityBreakdown30d ?? []).map(s => ({
      label: SEVERITY_NAME[s.severity],
      value: s.count,
      color: SEVERITY_COLOR[s.severity]
    }))
  );

  protected readonly cities = computed<BarItem[]>(() =>
    (this.data()?.topCities30d ?? []).map(c => ({ label: c.place, value: c.count }))
  );

  ngOnInit(): void {
    this.api.getDashboard().subscribe({
      next: data => this.data.set(data),
      error: () => this.failed.set(true)
    });
  }
}
