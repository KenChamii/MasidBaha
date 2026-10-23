import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import {
  AdminAuthService,
  AdminFloodReport,
  AdminReportStatus,
  AdminService,
  SessionTrustDto,
  timeAgo
} from '@masidbaha/shared';

type StatusFilter = 'all' | AdminReportStatus;
type TrustState = SessionTrustDto | 'loading' | 'error';

const STATUS_LABEL: Record<AdminReportStatus, string> = {
  1: 'Active',
  2: 'Resolved',
  3: 'Expired'
};

@Component({
  selector: 'app-reports-page',
  templateUrl: './reports-page.component.html',
  styleUrl: './reports-page.component.scss'
})
export class ReportsPageComponent implements OnInit {
  private readonly adminService = inject(AdminService);
  protected readonly auth = inject(AdminAuthService);

  protected readonly statusLabel = STATUS_LABEL;
  protected readonly timeAgo = timeAgo;

  protected readonly reports = signal<AdminFloodReport[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = 25;
  protected readonly filter = signal<StatusFilter>('all');
  protected readonly isLoading = signal(false);
  protected readonly actionErrors = signal<Record<string, string>>({});

  // Trust info loads per session, on click, instead of for every row on
  // page load. Most reports on a page share only a few sessions anyway.
  protected readonly trust = signal<Record<string, TrustState>>({});

  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize)));

  ngOnInit(): void {
    this.loadReports();
  }

  protected setFilter(filter: StatusFilter): void {
    this.filter.set(filter);
    this.page.set(1);
    this.loadReports();
  }

  protected goToPage(page: number): void {
    if (page < 1 || page > this.totalPages()) return;
    this.page.set(page);
    this.loadReports();
  }

  protected loadReports(): void {
    this.isLoading.set(true);
    const status = this.filter() === 'all' ? null : (this.filter() as AdminReportStatus);

    this.adminService.getReports(status, this.page(), this.pageSize).subscribe({
      next: result => {
        this.reports.set(result.reports);
        this.totalCount.set(result.totalCount);
        this.isLoading.set(false);
      },
      // A 401 is handled globally (back to the login page); other failures show a toast
      error: () => this.isLoading.set(false)
    });
  }

  protected verify(report: AdminFloodReport): void {
    this.setStatus(report, 2);
  }

  protected markExpired(report: AdminFloodReport): void {
    this.setStatus(report, 3);
  }

  protected reactivate(report: AdminFloodReport): void {
    this.setStatus(report, 1);
  }

  protected deleteReport(report: AdminFloodReport): void {
    if (!confirm('Sigurado ka bang tanggalin ito nang tuluyan? Hindi ito maaaring bawiin.')) return;

    this.clearActionError(report.id);
    this.adminService.delete(report.id).subscribe({
      next: () => this.loadReports(),
      error: (err: HttpErrorResponse) => this.setActionError(report.id, err, 'Nabigo ang pagtanggal. Subukan ulit.')
    });
  }

  // ---- session trust ----

  protected toggleTrust(sessionId: string): void {
    // Clicking again on an already loaded session collapses it, so this
    // button doubles as a show/hide toggle.
    const current = this.trust()[sessionId];
    if (current && current !== 'loading') {
      this.trust.update(map => {
        const { [sessionId]: _removed, ...rest } = map;
        return rest;
      });
      return;
    }

    this.setTrust(sessionId, 'loading');
    this.adminService.getSessionTrust(sessionId).subscribe({
      next: result => this.setTrust(sessionId, result),
      error: () => this.setTrust(sessionId, 'error')
    });
  }

  protected isTrustData(state: TrustState | undefined): state is SessionTrustDto {
    return !!state && state !== 'loading' && state !== 'error';
  }

  protected locationOf(report: AdminFloodReport): string {
    return report.city || report.province || report.region || `${report.lat.toFixed(4)}, ${report.lng.toFixed(4)}`;
  }

  private setStatus(report: AdminFloodReport, status: AdminReportStatus): void {
    this.clearActionError(report.id);
    this.adminService.setStatus(report.id, status).subscribe({
      next: () => this.loadReports(),
      error: (err: HttpErrorResponse) => this.setActionError(report.id, err, 'Nabigo ang aksyon. Subukan ulit.')
    });
  }

  private setTrust(sessionId: string, state: TrustState): void {
    this.trust.update(map => ({ ...map, [sessionId]: state }));
  }

  private clearActionError(reportId: string): void {
    this.actionErrors.update(map => {
      const { [reportId]: _removed, ...rest } = map;
      return rest;
    });
  }

  private setActionError(reportId: string, err: HttpErrorResponse, fallback: string): void {
    const message = err.status === 403 ? 'Your role is not allowed to do this.' : fallback;
    this.actionErrors.update(map => ({ ...map, [reportId]: message }));
  }
}
