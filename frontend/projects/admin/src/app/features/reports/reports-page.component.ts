import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Severity, ToastService, timeAgo } from '@masidbaha/shared';
import { AdminApiService } from '../../core/api/admin-api.service';
import { AdminAuthService } from '../../core/api/admin-auth.service';
import { AdminFloodReport, AdminReportQuery, AdminReportStatus, STATUS_LABEL } from '../../core/api/admin.models';
import { BADGE_BY_STATUS, SEVERITY_COLOR, SEVERITY_NAME, locationOf, problemMessage, saveBlob } from '../../core/util';
import { ConfirmService } from '../../ui/confirm.service';
import { PagerComponent } from '../../ui/pager.component';

type SortField = NonNullable<AdminReportQuery['sortBy']>;

@Component({
  selector: 'app-reports-page',
  imports: [FormsModule, RouterLink, PagerComponent],
  templateUrl: './reports-page.component.html'
})
export class ReportsPageComponent implements OnInit {
  private readonly api = inject(AdminApiService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  protected readonly auth = inject(AdminAuthService);

  protected readonly statusLabel = STATUS_LABEL;
  protected readonly badgeByStatus = BADGE_BY_STATUS;
  protected readonly severityName = SEVERITY_NAME;
  protected readonly severityColor = SEVERITY_COLOR;
  protected readonly severities: Severity[] = [1, 2, 3, 4];
  protected readonly timeAgo = timeAgo;
  protected readonly locationOf = locationOf;

  protected readonly reports = signal<AdminFloodReport[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = 25;
  protected readonly isLoading = signal(false);
  protected readonly isExporting = signal(false);

  // Filters (applied together; empty means "any")
  protected readonly search = signal('');
  protected readonly status = signal<AdminReportStatus | ''>('');
  protected readonly severity = signal<Severity | ''>('');
  protected readonly city = signal('');
  protected readonly from = signal('');
  protected readonly to = signal('');
  protected readonly hidden = signal<'' | 'true' | 'false'>('');

  protected readonly sortBy = signal<SortField>('reportedAt');
  protected readonly descending = signal(true);

  ngOnInit(): void {
    this.load();
  }

  protected applyFilters(): void {
    this.page.set(1);
    this.load();
  }

  protected clearFilters(): void {
    this.search.set('');
    this.status.set('');
    this.severity.set('');
    this.city.set('');
    this.from.set('');
    this.to.set('');
    this.hidden.set('');
    this.applyFilters();
  }

  protected sort(field: SortField): void {
    if (this.sortBy() === field) {
      this.descending.update(value => !value);
    } else {
      this.sortBy.set(field);
      this.descending.set(true);
    }
    this.page.set(1);
    this.load();
  }

  protected sortMark(field: SortField): string {
    return this.sortBy() === field ? (this.descending() ? ' ▼' : ' ▲') : '';
  }

  protected goToPage(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected load(): void {
    this.isLoading.set(true);
    this.api
      .getReports({
        ...this.filter(),
        page: this.page(),
        pageSize: this.pageSize,
        sortBy: this.sortBy(),
        descending: this.descending()
      })
      .subscribe({
        next: result => {
          this.reports.set(result.reports);
          this.totalCount.set(result.totalCount);
          this.isLoading.set(false);
        },
        // A 401 is handled globally (back to the login page); other failures show a toast
        error: () => this.isLoading.set(false)
      });
  }

  protected exportCsv(): void {
    this.isExporting.set(true);
    this.api.exportReports(this.filter()).subscribe({
      next: blob => {
        this.isExporting.set(false);
        saveBlob(blob, `masidbaha-reports-${new Date().toISOString().slice(0, 10)}.csv`);
      },
      error: err => {
        this.isExporting.set(false);
        this.toast.error(problemMessage(err, 'The export failed.'));
      }
    });
  }

  protected setStatus(report: AdminFloodReport, status: AdminReportStatus): void {
    this.api.setStatus(report.id, status).subscribe({
      next: () => this.load(),
      error: err => this.toast.error(problemMessage(err))
    });
  }

  protected toggleHidden(report: AdminFloodReport): void {
    this.api.setHidden(report.id, !report.isHidden).subscribe({
      next: () => this.load(),
      error: err => this.toast.error(problemMessage(err))
    });
  }

  protected async remove(report: AdminFloodReport): Promise<void> {
    const confirmed = await this.confirm.ask({
      title: 'Delete this report?',
      message: 'It is removed for good, together with its votes. This cannot be undone.',
      confirmLabel: 'Delete',
      danger: true
    });
    if (!confirmed) return;

    this.api.deleteReport(report.id).subscribe({
      next: () => {
        this.toast.success('Report deleted.');
        this.load();
      },
      error: err => this.toast.error(problemMessage(err))
    });
  }

  // The same filters feed the table, the map and the export
  private filter() {
    return {
      search: this.search().trim(),
      status: this.status() || null,
      severity: this.severity() || null,
      city: this.city().trim(),
      from: this.from() ? new Date(this.from()).toISOString() : '',
      // The "to" day is inclusive, so run to the end of it
      to: this.to() ? new Date(this.to() + 'T23:59:59').toISOString() : '',
      hidden: this.hidden() === '' ? null : this.hidden() === 'true'
    };
  }
}
