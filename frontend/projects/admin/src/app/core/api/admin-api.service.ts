import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { API_BASE_URL } from '@masidbaha/shared';
import { Observable } from 'rxjs';
import {
  AdminGetReportsResult,
  AdminMapPoint,
  AdminReportDetail,
  AdminReportFilter,
  AdminReportQuery,
  AdminReportStatus,
  AdminRole,
  AdminUserAccount,
  AuditEntry,
  AuditQuery,
  Ban,
  BanResult,
  Broadcast,
  BroadcastArea,
  BulkAction,
  BulkActionResult,
  CreateBanRequest,
  Dashboard,
  ModerationQueueItem,
  PagedResult,
  SessionTrust,
  SettingsUpdate,
  SettingsView,
  UserCredential
} from './admin.models';

// Drops empty values, so optional filters are simply left off the URL
function toParams(values: object): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(values)) {
    if (value !== null && value !== undefined && value !== '') params = params.set(key, String(value));
  }
  return params;
}

// Every call rides on the signed-in session cookie (see apiCredentialsInterceptor)
@Injectable({ providedIn: 'root' })
export class AdminApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${inject(API_BASE_URL)}/api/v1/admin`;

  // ---- reports ----

  getReports(query: AdminReportQuery): Observable<AdminGetReportsResult> {
    return this.http.get<AdminGetReportsResult>(`${this.baseUrl}/reports`, { params: toParams(query) });
  }

  getReportDetail(id: string): Observable<AdminReportDetail> {
    return this.http.get<AdminReportDetail>(`${this.baseUrl}/reports/${id}`);
  }

  getMapPoints(filter: AdminReportFilter): Observable<AdminMapPoint[]> {
    return this.http.get<AdminMapPoint[]>(`${this.baseUrl}/reports/map`, { params: toParams(filter) });
  }

  // The CSV is fetched through the HTTP client (so the session cookie is sent) and saved by the page
  exportReports(filter: AdminReportFilter): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/reports/export`, { params: toParams(filter), responseType: 'blob' });
  }

  setStatus(id: string, status: AdminReportStatus): Observable<void> {
    return this.http.patch<void>(`${this.baseUrl}/reports/${id}/status`, { status });
  }

  setHidden(id: string, hidden: boolean): Observable<void> {
    return this.http.patch<void>(`${this.baseUrl}/reports/${id}/hidden`, { hidden });
  }

  deleteReport(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/reports/${id}`);
  }

  getSessionTrust(sessionId: string): Observable<SessionTrust> {
    return this.http.get<SessionTrust>(`${this.baseUrl}/sessions/${encodeURIComponent(sessionId)}/trust`);
  }

  // ---- dashboard & moderation ----

  getDashboard(): Observable<Dashboard> {
    return this.http.get<Dashboard>(`${this.baseUrl}/dashboard`);
  }

  getQueue(page: number, pageSize: number): Observable<PagedResult<ModerationQueueItem>> {
    return this.http.get<PagedResult<ModerationQueueItem>>(`${this.baseUrl}/moderation/queue`, {
      params: toParams({ page, pageSize })
    });
  }

  bulk(ids: string[], action: BulkAction): Observable<BulkActionResult> {
    return this.http.post<BulkActionResult>(`${this.baseUrl}/moderation/bulk`, { ids, action });
  }

  // ---- bans ----

  getBans(page: number, pageSize: number, active: boolean | null): Observable<PagedResult<Ban>> {
    return this.http.get<PagedResult<Ban>>(`${this.baseUrl}/bans`, { params: toParams({ page, pageSize, active }) });
  }

  createBan(request: CreateBanRequest): Observable<BanResult> {
    return this.http.post<BanResult>(`${this.baseUrl}/bans`, request);
  }

  liftBan(id: string): Observable<Ban> {
    return this.http.delete<Ban>(`${this.baseUrl}/bans/${id}`);
  }

  // ---- broadcasts, audit, settings ----

  getBroadcasts(page: number, pageSize: number): Observable<PagedResult<Broadcast>> {
    return this.http.get<PagedResult<Broadcast>>(`${this.baseUrl}/broadcasts`, {
      params: toParams({ page, pageSize })
    });
  }

  sendBroadcast(title: string, body: string, url?: string, area?: BroadcastArea | null): Observable<Broadcast> {
    return this.http.post<Broadcast>(`${this.baseUrl}/broadcasts`, {
      title,
      body,
      url: url || null,
      areaLat: area?.lat ?? null,
      areaLng: area?.lng ?? null,
      areaRadiusMeters: area?.radiusMeters ?? null
    });
  }

  getAudit(query: AuditQuery): Observable<PagedResult<AuditEntry>> {
    return this.http.get<PagedResult<AuditEntry>>(`${this.baseUrl}/audit`, { params: toParams(query) });
  }

  getSettings(): Observable<SettingsView> {
    return this.http.get<SettingsView>(`${this.baseUrl}/settings`);
  }

  saveSettings(update: SettingsUpdate): Observable<SettingsView> {
    return this.http.put<SettingsView>(`${this.baseUrl}/settings`, update);
  }

  resetSettings(): Observable<SettingsView> {
    return this.http.delete<SettingsView>(`${this.baseUrl}/settings`);
  }

  // ---- staff accounts ----

  getUsers(page: number, pageSize: number, search: string): Observable<PagedResult<AdminUserAccount>> {
    return this.http.get<PagedResult<AdminUserAccount>>(`${this.baseUrl}/users`, {
      params: toParams({ page, pageSize, search })
    });
  }

  createUser(email: string, displayName: string, role: AdminRole): Observable<UserCredential> {
    return this.http.post<UserCredential>(`${this.baseUrl}/users`, { email, displayName, role });
  }

  updateUser(id: string, changes: { displayName?: string; role?: AdminRole }): Observable<AdminUserAccount> {
    return this.http.patch<AdminUserAccount>(`${this.baseUrl}/users/${id}`, changes);
  }

  deactivateUser(id: string): Observable<AdminUserAccount> {
    return this.http.post<AdminUserAccount>(`${this.baseUrl}/users/${id}/deactivate`, {});
  }

  reactivateUser(id: string): Observable<AdminUserAccount> {
    return this.http.post<AdminUserAccount>(`${this.baseUrl}/users/${id}/reactivate`, {});
  }

  resetPassword(id: string): Observable<UserCredential> {
    return this.http.post<UserCredential>(`${this.baseUrl}/users/${id}/reset-password`, {});
  }

  resetTwoFactor(id: string): Observable<AdminUserAccount> {
    return this.http.post<AdminUserAccount>(`${this.baseUrl}/users/${id}/reset-2fa`, {});
  }
}
