import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';

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
    imports: [CommonModule, FormsModule],
    templateUrl: './admin-page.component.html',
    styleUrls: ['./admin-page.component.scss']
})
export class AdminPageComponent implements OnInit {
  readonly statusLabel = STATUS_LABEL;

  emailInput = '';
  passwordInput = '';
  isSigningIn = false;
  isRestoring = true;
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

  constructor(public auth: AdminAuthService, private adminService: AdminService) {}

  ngOnInit(): void {
    // The session cookie survives a reload, so ask the API who is signed in
    this.auth.restore().subscribe(user => {
      this.isRestoring = false;
      if (user) this.loadReports();
    });
  }

  signIn(): void {
    if (!this.emailInput.trim() || !this.passwordInput || this.isSigningIn) return;

    this.isSigningIn = true;
    this.authError = null;

    this.auth.login(this.emailInput.trim(), this.passwordInput).subscribe({
      next: () => {
        this.isSigningIn = false;
        this.passwordInput = '';
        this.loadReports();
      },
      error: (err: HttpErrorResponse) => {
        this.isSigningIn = false;
        // The API's problem details carry a safe, human-readable message
        this.authError = err.error?.detail
          ?? (err.status === 429 ? 'Too many attempts. Please wait a moment.' : 'Could not sign in. Please try again.');
      }
    });
  }

  signOut(): void {
    this.auth.logout().subscribe(() => this.reports = []);
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
      error: (err: HttpErrorResponse) => {
        this.isLoading = false;
        if (err.status === 401) {
          // The session ended (expired, signed out elsewhere, password changed):
          // go back to the sign-in screen instead of showing an empty table
          this.authError = 'Your session has ended. Please sign in again.';
          this.auth.clearLocalSession();
          this.reports = [];
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
      error: (err: HttpErrorResponse) => this.actionErrorByReportId[report.id] = this.actionError(err, 'Nabigo ang aksyon. Subukan ulit.')
    });
  }

  deleteReport(report: AdminFloodReport): void {
    if (!confirm('Sigurado ka bang tanggalin ito nang tuluyan? Hindi ito maaaring bawiin.')) return;

    delete this.actionErrorByReportId[report.id];
    this.adminService.delete(report.id).subscribe({
      next: () => this.loadReports(),
      error: (err: HttpErrorResponse) => this.actionErrorByReportId[report.id] = this.actionError(err, 'Nabigo ang pagtanggal. Subukan ulit.')
    });
  }

  private actionError(err: HttpErrorResponse, fallback: string): string {
    return err.status === 403 ? 'Your role is not allowed to do this.' : fallback;
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
