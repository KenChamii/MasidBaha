import { DatePipe, JsonPipe } from '@angular/common';
import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ToastService, timeAgo } from '@masidbaha/shared';
import { AdminApiService } from '../../core/api/admin-api.service';
import { AdminAuthService } from '../../core/api/admin-auth.service';
import {
  AdminReportDetail,
  AdminReportStatus,
  BanKind,
  BanMode,
  FLAG_LABEL,
  STATUS_LABEL
} from '../../core/api/admin.models';
import { BADGE_BY_STATUS, SEVERITY_COLOR, SEVERITY_NAME, locationOf, problemMessage } from '../../core/util';
import { ConfirmService } from '../../ui/confirm.service';

@Component({
  selector: 'app-report-detail-page',
  imports: [FormsModule, RouterLink, DatePipe, JsonPipe],
  templateUrl: './report-detail-page.component.html'
})
export class ReportDetailPageComponent implements OnInit {
  private readonly api = inject(AdminApiService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly router = inject(Router);
  protected readonly auth = inject(AdminAuthService);

  // Bound from the route (:id) by withComponentInputBinding
  readonly id = input.required<string>();

  protected readonly statusLabel = STATUS_LABEL;
  protected readonly badgeByStatus = BADGE_BY_STATUS;
  protected readonly severityName = SEVERITY_NAME;
  protected readonly severityColor = SEVERITY_COLOR;
  protected readonly flagLabel = FLAG_LABEL;
  protected readonly timeAgo = timeAgo;
  protected readonly locationOf = locationOf;

  protected readonly detail = signal<AdminReportDetail | null>(null);
  protected readonly notFound = signal(false);

  // Ban form
  protected readonly banOpen = signal(false);
  protected readonly banKind = signal<BanKind>(1);
  protected readonly banMode = signal<BanMode>(2);
  protected readonly banReason = signal('');
  protected readonly banHours = signal<number | null>(null);
  protected readonly banHide = signal(true);
  protected readonly banBusy = signal(false);
  protected readonly banError = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  protected fullPlace(report: { city?: string; province?: string; region?: string }): string {
    return [report.city, report.province, report.region].filter(Boolean).join(', ') || '—';
  }

  protected load(): void {
    this.api.getReportDetail(this.id()).subscribe({
      next: detail => this.detail.set(detail),
      error: err => {
        if (err.status === 404) this.notFound.set(true);
      }
    });
  }

  protected setStatus(status: AdminReportStatus): void {
    this.api.setStatus(this.id(), status).subscribe({
      next: () => {
        this.toast.success('Status updated.');
        this.load();
      },
      error: err => this.toast.error(problemMessage(err))
    });
  }

  protected toggleHidden(hidden: boolean): void {
    this.api.setHidden(this.id(), !hidden).subscribe({
      next: () => this.load(),
      error: err => this.toast.error(problemMessage(err))
    });
  }

  protected async remove(): Promise<void> {
    const confirmed = await this.confirm.ask({
      title: 'Delete this report?',
      message: 'It is removed for good, together with its votes. This cannot be undone.',
      confirmLabel: 'Delete',
      danger: true
    });
    if (!confirmed) return;

    this.api.deleteReport(this.id()).subscribe({
      next: () => {
        this.toast.success('Report deleted.');
        void this.router.navigate(['/reports']);
      },
      error: err => this.toast.error(problemMessage(err))
    });
  }

  protected submitBan(): void {
    if (this.banBusy()) return;

    this.banBusy.set(true);
    this.banError.set(null);

    this.api
      .createBan({
        reportId: this.id(),
        kind: this.banKind(),
        mode: this.banMode(),
        reason: this.banReason().trim(),
        expiresInHours: this.banHours(),
        hideExistingReports: this.banHide()
      })
      .subscribe({
        next: result => {
          this.banBusy.set(false);
          this.banOpen.set(false);
          this.banReason.set('');
          this.toast.success(
            result.hiddenReportIds.length ? `Banned. ${result.hiddenReportIds.length} report(s) hidden.` : 'Banned.'
          );
          this.load();
        },
        error: err => {
          this.banBusy.set(false);
          this.banError.set(problemMessage(err, 'Could not create the ban.'));
        }
      });
  }

  protected liftBan(id: string): void {
    this.api.liftBan(id).subscribe({
      next: () => {
        this.toast.success('Ban lifted.');
        this.load();
      },
      error: err => this.toast.error(problemMessage(err))
    });
  }
}
