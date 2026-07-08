import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { FloodReport, CreateFloodReportRequest, TopReportsQuery, HeatmapPoint, HeatmapQuery } from '../../../shared/models/flood-report.model';
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

  getTop(query: TopReportsQuery): Observable<FloodReport[]> {
    const params: Record<string, string | number> = { limit: query.limit ?? 20 };
    if (query.region) params['region'] = query.region;
    if (query.province) params['province'] = query.province;
    if (query.city) params['city'] = query.city;

    return this.http.get<FloodReport[]>(`${this.baseUrl}/top`, { params });
  }

  create(request: CreateFloodReportRequest): Observable<FloodReport> {
    return this.http.post<FloodReport>(this.baseUrl, request);
  }

  vote(reportId: string, voteType: 1 | -1, voterSessionId: string): Observable<unknown> {
    return this.http.post(`${this.baseUrl}/${reportId}/vote`, { voterSessionId, voteType });
  }

  getHeatmap(query: HeatmapQuery = {}): Observable<HeatmapPoint[]> {
    const params: Record<string, string> = {};
    if (query.fromDate) params['fromDate'] = query.fromDate;
    if (query.toDate) params['toDate'] = query.toDate;
    if (query.region) params['region'] = query.region;
    if (query.province) params['province'] = query.province;
    if (query.city) params['city'] = query.city;

    return this.http.get<HeatmapPoint[]>(`${this.baseUrl}/heatmap`, { params });
  }
}