import { Injectable, inject } from '@angular/core';
import { API_BASE_URL } from './api-config';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AdminGetReportsResult, AdminReportStatus, SessionTrustDto } from '../models/admin.model';

// Every call rides on the signed-in session cookie (see apiCredentialsInterceptor)
@Injectable({ providedIn: 'root' })
export class AdminService {
  private http = inject(HttpClient);

  private readonly baseUrl = `${inject(API_BASE_URL)}/api/v1/admin`;

  getReports(status: AdminReportStatus | null, page = 1, pageSize = 25): Observable<AdminGetReportsResult> {
    const params: Record<string, string | number> = { page, pageSize };
    if (status !== null) params['status'] = status;

    return this.http.get<AdminGetReportsResult>(`${this.baseUrl}/reports`, { params });
  }

  setStatus(id: string, status: AdminReportStatus): Observable<void> {
    return this.http.patch<void>(`${this.baseUrl}/reports/${id}/status`, { status });
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/reports/${id}`);
  }

  getSessionTrust(sessionId: string): Observable<SessionTrustDto> {
    return this.http.get<SessionTrustDto>(`${this.baseUrl}/sessions/${sessionId}/trust`);
  }
}
