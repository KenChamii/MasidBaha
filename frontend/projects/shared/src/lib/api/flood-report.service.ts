import { Injectable, inject } from '@angular/core';
import { API_BASE_URL } from './api-config';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  FloodReport,
  CreateFloodReportRequest,
  TopReportsQuery,
  HeatmapPoint,
  HeatmapQuery
} from '../models/flood-report.model';

@Injectable({ providedIn: 'root' })
export class FloodReportService {
  private http = inject(HttpClient);

  private readonly baseUrl = `${inject(API_BASE_URL)}/api/v1/flood-reports`;

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

  // The voter is identified by the server's session cookie, not by anything sent here
  vote(reportId: string, voteType: 1 | -1): Observable<unknown> {
    return this.http.post(`${this.baseUrl}/${reportId}/vote`, { voteType });
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
