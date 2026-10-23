import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { AdminGetReportsResult, AdminReportStatus, SessionTrustDto } from '../admin.model';

// Every call rides on the signed-in session cookie (see apiCredentialsInterceptor)
@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly baseUrl = `${environment.apiUrl}/api/v1/admin`;

  constructor(private http: HttpClient) {}

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
