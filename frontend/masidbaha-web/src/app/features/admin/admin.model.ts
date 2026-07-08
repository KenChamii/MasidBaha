export type AdminReportStatus = 1 | 2 | 3; // Active, Resolved, Expired

export interface AdminFloodReport {
  id: string;
  lat: number;
  lng: number;
  severity: 1 | 2 | 3 | 4;
  notes?: string;
  photoUrl?: string;
  reportedAt: string;
  expiresAt: string;
  confidenceScore: number;
  status: AdminReportStatus;
  reporterSessionId: string;
  region?: string;
  province?: string;
  city?: string;
}

export interface AdminGetReportsResult {
  reports: AdminFloodReport[];
  totalCount: number;
  page: number;
  pageSize: number;
}
