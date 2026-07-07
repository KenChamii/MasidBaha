import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { FloodReport, CreateFloodReportRequest } from '../../../shared/models/flood-report.model';
import { environment } from '../../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class FloodReportService {
  private readonly baseUrl = `${environment.apiUrl}/api/flood-reports`;

  constructor(private http: HttpClient) {}

  getNearby(lat: number, lng: number, radiusMeters = 5000): Observable<FloodReport[]> {
    return this.http.get<FloodReport[]>(this.baseUrl, {
      params: { lat, lng, radiusMeters }
    });
  }

  create(request: CreateFloodReportRequest): Observable<FloodReport> {
    return this.http.post<FloodReport>(this.baseUrl, request);
  }

  vote(reportId: string, voteType: 1 | -1, voterSessionId: string): Observable<unknown> {
    return this.http.post(`${this.baseUrl}/${reportId}/vote`, { voterSessionId, voteType });
  }
}