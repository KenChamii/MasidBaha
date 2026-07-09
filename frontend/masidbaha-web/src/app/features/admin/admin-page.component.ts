import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

import { AdminAuthService } from '../../core/services/admin-auth.service';
import { AdminService } from './services/admin.service';
import { AdminFloodReport, AdminReportStatus, SessionTrustDto } from './admin.model';

type StatusFilter = 'all' | AdminReportStatus;

const STATUS_LABEL: Record<AdminReportStatus, string> = {
  1: 'Active',
  2: 'Resolved',
  3: 'Expired'
};

@Component({
  selector: 'app-admin-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-page.component.html',
  styleUrls: ['./admin-page.component.scss']
})
export class AdminPageComponent implements OnInit {
  readonly statusLabel = STATUS_LABEL;

  apiKeyInput = '';
  isUnlocked = false;
  authError: string | null = null;

  reports: AdminFloodReport[] = [];
  totalCount = 0;
  page = 1;
  pageSize = 25;
  filter: StatusFilter = 'all';
  isLoading = false;
  actionErrorByReportId: Record<string, string | undefined> = {};

  // Trust info loads per session, on click, instead of for every row on
  // page load. Most reports on a page share only a few sessions anyway.
  trustBySessionId: Record<string, SessionTrustDto | 'loading' | 'error' | undefined> = {};

  constructor(private adminAuth: AdminAuthService, private adminService: AdminService) {}

  ngOnInit(): void {
    if (this.adminAuth.hasApiKey) {
      this.isUnlocked = true;
      this.loadReports();
    }
  }

  unlock(): void {
    if (!this.apiKeyInput.trim()) return;
    this.adminAuth.setApiKey(this.apiKeyInput.trim());
    this.isUnlocked = true;
    this.authError = null;
    this.loadReports();
  }

  lock(): void {
    this.adminAuth.clearApiKey();
    this.isUnlocked = false;
    this.reports = [];
  }

  setFilter(filter: StatusFilter): void {
    this.filter = filter;
    this.page = 1;
    this.loadReports();
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages) return;
    this.page = page;
    this.loadReports();
  }

  get totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount / this.pageSize));
  }

  loadReports(): void {
    this.isLoading = true;
    this.authError = null;

    const statusParam = this.filter === 'all' ? null : this.filter;

    this.adminService.getReports(statusParam, this.page, this.pageSize).subscribe({
      next: result => {
        this.reports = result.reports;
        this.totalCount = result.totalCount;
        this.isLoading = false;
      },
      error: err => {
        this.isLoading = false;
        if (err.status === 401 || err.status === 503) {
          // Wrong/missing key, or admin isn't configured on the backend —
          // either way, drop back to the unlock screen rather than show an
          // empty table with no explanation.
          this.authError = err.status === 503
            ? 'Hindi pa naka-configure ang admin panel sa backend (Admin:ApiKey).'
            : 'Maling API key.';
          this.lock();
        }
      }
    });
  }

  verify(report: AdminFloodReport): void {
    this.setStatus(report, 2);
  }

  markExpired(report: AdminFloodReport): void {
    this.setStatus(report, 3);
  }

  reactivate(report: AdminFloodReport): void {
    this.setStatus(report, 1);
  }

  private setStatus(report: AdminFloodReport, status: AdminReportStatus): void {
    delete this.actionErrorByReportId[report.id];
    this.adminService.setStatus(report.id, status).subscribe({
      next: () => this.loadReports(),
      error: () => this.actionErrorByReportId[report.id] = 'Nabigo ang aksyon. Subukan ulit.'
    });
  }

  deleteReport(report: AdminFloodReport): void {
    if (!confirm('Sigurado ka bang tanggalin ito nang tuluyan? Hindi ito maaaring bawiin.')) return;

    delete this.actionErrorByReportId[report.id];
    this.adminService.delete(report.id).subscribe({
      next: () => this.loadReports(),
      error: () => this.actionErrorByReportId[report.id] = 'Nabigo ang pagtanggal. Subukan ulit.'
    });
  }

  // ---- session trust ----

  trustFor(sessionId: string): SessionTrustDto | 'loading' | 'error' | undefined {
    return this.trustBySessionId[sessionId];
  }

  toggleTrust(sessionId: string): void {
    // Clicking again on an already loaded session collapses it, so this
    // button doubles as a show/hide toggle.
    if (this.trustBySessionId[sessionId] && this.trustBySessionId[sessionId] !== 'loading') {
      delete this.trustBySessionId[sessionId];
      return;
    }

    this.trustBySessionId[sessionId] = 'loading';
    this.adminService.getSessionTrust(sessionId).subscribe({
      next: trust => this.trustBySessionId[sessionId] = trust,
      error: () => this.trustBySessionId[sessionId] = 'error'
    });
  }

  timeAgo(isoDate: string): string {
    const diffMs = Date.now() - new Date(isoDate).getTime();
    const minutes = Math.floor(diffMs / 60000);
    if (minutes < 1) return 'ngayon lang';
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    return `${Math.floor(hours / 24)}d ago`;
  }
}
