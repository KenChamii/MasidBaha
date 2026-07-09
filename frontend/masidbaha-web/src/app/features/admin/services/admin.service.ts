import { Injectable } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../../environments/environment';
import { AdminAuthService } from '../../../core/services/admin-auth.service';
import { AdminGetReportsResult, AdminReportStatus, SessionTrustDto } from '../admin.model';

@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly baseUrl = `${environment.apiUrl}/api/admin`;

  constructor(private http: HttpClient, private adminAuth: AdminAuthService) {}

  private get headers(): HttpHeaders {
    return new HttpHeaders({ 'X-Admin-Key': this.adminAuth.apiKey ?? '' });
  }

  getReports(status: AdminReportStatus | null, page = 1, pageSize = 25): Observable<AdminGetReportsResult> {
    const params: Record<string, string | number> = { page, pageSize };
    if (status !== null) params['status'] = status;

    return this.http.get<AdminGetReportsResult>(`${this.baseUrl}/reports`, {
      headers: this.headers,
      params
    });
  }

  setStatus(id: string, status: AdminReportStatus): Observable<void> {
    return this.http.patch<void>(`${this.baseUrl}/reports/${id}/status`, { status }, {
      headers: this.headers
    });
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/reports/${id}`, {
      headers: this.headers
    });
  }

  getSessionTrust(sessionId: string): Observable<SessionTrustDto> {
    return this.http.get<SessionTrustDto>(`${this.baseUrl}/sessions/${sessionId}/trust`, {
      headers: this.headers
    });
  }
}
