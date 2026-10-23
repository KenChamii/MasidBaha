import { Injectable, inject } from '@angular/core';
import { API_BASE_URL } from './api-config';
import { HttpClient, HttpContext } from '@angular/common/http';
import { Observable } from 'rxjs';
import { SKIP_ERROR_TOAST } from '../core/error-toast.interceptor';
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

  // One report by id (what a shared link opens). 404 when it is unknown or has been hidden.
  getById(id: string): Observable<FloodReport> {
    return this.http.get<FloodReport>(`${this.baseUrl}/${id}`);
  }

  getTop(query: TopReportsQuery): Observable<FloodReport[]> {
    const params: Record<string, string | number> = { limit: query.limit ?? 20 };
    if (query.region) params['region'] = query.region;
    if (query.province) params['province'] = query.province;
    if (query.city) params['city'] = query.city;

    return this.http.get<FloodReport[]>(`${this.baseUrl}/top`, { params });
  }

  // quiet: the caller reports a failure itself (no error toast from the interceptor)
  create(request: CreateFloodReportRequest, options: { quiet?: boolean } = {}): Observable<FloodReport> {
    const context = options.quiet ? new HttpContext().set(SKIP_ERROR_TOAST, true) : undefined;
    return this.http.post<FloodReport>(this.baseUrl, request, { context });
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
