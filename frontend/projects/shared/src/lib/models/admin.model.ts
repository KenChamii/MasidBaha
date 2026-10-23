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

// Matches SessionTrustDto on the backend (Trust/SessionTrustService.cs).
// trustScore is null for a session with no reports yet, which is different
// from 0 (meaning it has reports but none got confirmed).
export interface SessionTrustDto {
  sessionId: string;
  totalReports: number;
  resolvedReports: number;
  activeReports: number;
  avgConfidenceScore: number;
  firstReportAt: string | null;
  lastReportAt: string | null;
  trustScore: number | null;
}
