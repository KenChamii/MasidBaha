export type Severity = 1 | 2 | 3 | 4;
export type ReportStatus = 1 | 2 | 3;

export interface FloodReport {
  id: string;
  lat: number;
  lng: number;
  severity: Severity;
  notes?: string;
  photoUrl?: string;
  reportedAt: string;
  confidenceScore: number;
  status: ReportStatus;
}

export interface CreateFloodReportRequest {
  lat: number;
  lng: number;
  severity: Severity;
  notes?: string;
  photoUrl?: string;
  reporterSessionId: string;
}