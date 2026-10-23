// Shapes of the back-office API (see backend/MasidBaha.Application/Admin)
import { Severity } from '@masidbaha/shared';

export type AdminRole = 'SuperAdmin' | 'Moderator' | 'Viewer';
export const ADMIN_ROLES: AdminRole[] = ['SuperAdmin', 'Moderator', 'Viewer'];

export type AdminReportStatus = 1 | 2 | 3; // Active, Resolved, Expired
export const STATUS_LABEL: Record<AdminReportStatus, string> = { 1: 'Active', 2: 'Resolved', 3: 'Expired' };

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

// ---- reports ----

export interface AdminFloodReport {
  id: string;
  lat: number;
  lng: number;
  severity: Severity;
  notes?: string;
  photoUrl?: string;
  reportedAt: string;
  expiresAt: string;
  confidenceScore: number;
  status: AdminReportStatus;
  reporterSessionId: string;
  reporterIpFingerprint?: string;
  isHidden: boolean;
  region?: string;
  province?: string;
  city?: string;
}

export interface AdminReportFilter {
  search?: string;
  status?: AdminReportStatus | null;
  severity?: Severity | null;
  region?: string;
  province?: string;
  city?: string;
  from?: string;
  to?: string;
  hidden?: boolean | null;
}

export interface AdminReportQuery extends AdminReportFilter {
  page: number;
  pageSize: number;
  sortBy?: 'reportedAt' | 'confidence' | 'severity' | 'status' | 'expiresAt';
  descending?: boolean;
}

export interface AdminGetReportsResult {
  reports: AdminFloodReport[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface AdminMapPoint {
  id: string;
  lat: number;
  lng: number;
  severity: Severity;
  status: AdminReportStatus;
  confidenceScore: number;
  reportedAt: string;
  city?: string;
  isHidden: boolean;
}

// trustScore is null for a session with no reports yet, which is different
// from 0 (it has reports but none got confirmed).
export interface SessionTrust {
  sessionId: string;
  totalReports: number;
  resolvedReports: number;
  activeReports: number;
  avgConfidenceScore: number;
  firstReportAt: string | null;
  lastReportAt: string | null;
  trustScore: number | null;
}

export interface AdminVote {
  voterSessionId: string;
  voteType: 1 | -1;
  votedAt: string;
}

export interface AdminReportDetail {
  report: AdminFloodReport;
  votes: AdminVote[];
  reporterTrust: SessionTrust;
  flags: string[];
  activeBans: Ban[];
  history: AuditEntry[];
}

// ---- moderation ----

export const FLAG_LABEL: Record<string, string> = {
  low_trust_reporter: 'Low-trust reporter',
  many_denies: 'Many "it\'s gone" votes',
  reporter_flooding: 'Reporter flooding',
  hidden: 'Shadow-banned source'
};

export interface ModerationQueueItem {
  report: AdminFloodReport;
  flags: string[];
  denyVotes: number;
  confirmVotes: number;
}

export type BulkAction = 1 | 2 | 3 | 4 | 5 | 6; // Resolve, Expire, Reactivate, Delete, Hide, Unhide

export interface BulkActionResult {
  action: BulkAction;
  affectedIds: string[];
  notFoundIds: string[];
}

// ---- bans ----

export type BanKind = 1 | 2; // Session, Ip
export type BanMode = 1 | 2; // Block, Shadow

export interface Ban {
  id: string;
  kind: BanKind;
  mode: BanMode;
  target: string;
  reason: string;
  createdAt: string;
  createdByEmail: string;
  expiresAt: string | null;
  liftedAt: string | null;
  liftedByEmail: string | null;
  isActive: boolean;
}

export interface CreateBanRequest {
  reportId?: string;
  value?: string;
  kind: BanKind;
  mode: BanMode;
  reason: string;
  expiresInHours?: number | null;
  hideExistingReports: boolean;
}

export interface BanResult {
  ban: Ban;
  hiddenReportIds: string[];
}

// ---- dashboard ----

export interface Dashboard {
  generatedAt: string;
  activeReports: number;
  reportsLast24h: number;
  reportsPrevious24h: number;
  reportsLast7d: number;
  reportsPrevious7d: number;
  votesLast24h: number;
  pushSubscribers: number;
  flaggedReports: number;
  resolvedShare30d: number | null;
  severityBreakdown30d: { severity: Severity; count: number }[];
  topCities30d: { place: string; count: number }[];
  dailyReports30d: { day: string; count: number }[];
}

// ---- broadcasts, audit, settings ----

export interface Broadcast {
  id: string;
  title: string;
  body: string;
  url: string;
  sentAt: string;
  sentByEmail: string;
  recipients: number;
}

export interface AuditEntry {
  id: string;
  at: string;
  actorEmail: string;
  action: string;
  targetType: string;
  targetId: string | null;
  details: Record<string, unknown> | null;
}

export interface AuditQuery {
  page: number;
  pageSize: number;
  action?: string;
  actor?: string;
  targetType?: string;
  targetId?: string;
  from?: string;
  to?: string;
}

export interface SettingsSnapshot {
  reportLifetimeHours: number;
  pushMinimumSeverity: Severity;
  denyFlagThreshold: number;
  floodingReportsPerHour: number;
  reportWritesPerMinute: number;
  voteWritesPerMinute: number;
  version: number;
}

export type SettingsUpdate = Omit<SettingsSnapshot, 'version'>;

export interface SettingsView {
  current: SettingsSnapshot;
  defaults: SettingsSnapshot;
  overridden: string[];
}

// ---- staff accounts ----

export interface AdminUserAccount {
  id: string;
  email: string;
  displayName: string;
  role: AdminRole;
  isActive: boolean;
  mustChangePassword: boolean;
  twoFactorEnabled: boolean;
  createdAt: string;
  lastLoginAt: string | null;
}

export interface UserCredential {
  user: AdminUserAccount;
  temporaryPassword: string;
}

// ---- signed-in staff member ----

export interface AdminUser {
  id: string;
  email: string;
  displayName: string;
  roles: AdminRole[];
  mustChangePassword: boolean;
  twoFactorEnabled: boolean;
}

export interface TwoFactorSetup {
  sharedKey: string;
  otpAuthUri: string;
}
