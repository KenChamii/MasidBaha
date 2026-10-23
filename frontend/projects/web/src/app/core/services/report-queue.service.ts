import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { FloodReportService, I18nService, PhotoService, ToastService } from '@masidbaha/shared';
import { Subject, firstValueFrom, fromEvent, interval, merge } from 'rxjs';
import { QueuedReport, ReportQueueStorage } from './report-queue.storage';

export const MAX_QUEUED_REPORTS = 20;
// A report that keeps failing for a reason that is not the connection is given up on, not retried forever
export const MAX_ATTEMPTS = 8;
const RETRY_EVERY_MS = 30_000;

// A new id for a report. crypto.randomUUID needs a secure page (HTTPS or localhost), so there is a fallback.
export function newRequestId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, byte => byte.toString(16).padStart(2, '0')).join('');
  return [hex.slice(0, 8), hex.slice(8, 12), hex.slice(12, 16), hex.slice(16, 20), hex.slice(20)].join('-');
}

// What a report needs to be saved for later
export type ReportDraft = Pick<QueuedReport, 'id' | 'lat' | 'lng' | 'severity'> & { notes?: string; photo?: File };

// Worth trying again later: no connection, a server that is waking up or busy. Anything else (a refused photo, a
// report outside the Philippines, a blocked device) will not get better by waiting.
export function isRetryable(error: unknown): boolean {
  if (!(error instanceof HttpErrorResponse)) return true;
  return [0, 408, 429, 500, 502, 503, 504].includes(error.status);
}

// Reports made without a connection. They are kept on the device and sent, in order, as soon as the app can
// reach the server again. Each carries its own id, so the server saves it once even if a send is repeated.
@Injectable({ providedIn: 'root' })
export class ReportQueueService {
  private readonly storage = inject(ReportQueueStorage);
  private readonly reports = inject(FloodReportService);
  private readonly photos = inject(PhotoService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);

  private readonly queue = signal<QueuedReport[]>([]);
  private flushing = false;
  private started = false;

  // What is waiting to be sent (oldest first)
  readonly pending = this.queue.asReadonly();

  // Emits after a queued report reached the server, so the map can refresh
  readonly sent$ = new Subject<void>();

  // Load what an earlier visit left behind, then watch for the connection coming back
  async start(): Promise<void> {
    if (this.started) return;
    this.started = true;

    this.queue.set(await this.storage.list());

    merge(fromEvent(window, 'online'), fromEvent(document, 'visibilitychange'), interval(RETRY_EVERY_MS)).subscribe(
      () => {
        if (this.queue().length > 0) void this.flush();
      }
    );

    await this.flush();
  }

  // false when too many are already waiting
  async enqueue(draft: ReportDraft): Promise<boolean> {
    if (this.queue().length >= MAX_QUEUED_REPORTS) return false;

    const report: QueuedReport = {
      id: draft.id,
      lat: draft.lat,
      lng: draft.lng,
      severity: draft.severity,
      notes: draft.notes,
      photo: draft.photo,
      photoName: draft.photo?.name,
      createdAt: new Date().toISOString(),
      attempts: 0
    };

    await this.storage.put(report);
    this.queue.update(list => [...list.filter(item => item.id !== report.id), report]);
    return true;
  }

  async flush(): Promise<void> {
    if (this.flushing || navigator.onLine === false) return;
    this.flushing = true;
    try {
      for (const report of [...this.queue()]) {
        // Oldest first; when one cannot be sent right now, the rest wait their turn
        if ((await this.send(report)) === 'retry') break;
      }
    } finally {
      this.flushing = false;
    }
  }

  private async send(report: QueuedReport): Promise<'sent' | 'retry' | 'dropped'> {
    let current = report;

    try {
      if (current.photo && !current.photoUrl) {
        try {
          const file = new File([current.photo], current.photoName ?? 'photo.jpg', { type: current.photo.type });
          const upload = await firstValueFrom(this.photos.upload(file, { quiet: true }));
          current = { ...current, photoUrl: upload.url };
          // Remembered, so a retry of the report does not upload the photo a second time
          await this.update(current);
        } catch (error) {
          if (isRetryable(error)) throw error;
          // The photo itself was refused: the report is worth more than the photo
          current = { ...current, photo: undefined, photoName: undefined };
        }
      }

      await firstValueFrom(
        this.reports.create(
          {
            lat: current.lat,
            lng: current.lng,
            severity: current.severity,
            notes: current.notes,
            photoUrl: current.photoUrl,
            clientRequestId: current.id
          },
          { quiet: true }
        )
      );
    } catch (error) {
      if (isRetryable(error)) {
        if (current.attempts + 1 >= MAX_ATTEMPTS) return this.drop(current);
        await this.update({ ...current, attempts: current.attempts + 1 });
        return 'retry';
      }
      return this.drop(current);
    }

    await this.remove(current.id);
    this.toast.success(this.i18n.t('queue.sent'));
    this.sent$.next();
    return 'sent';
  }

  private async drop(report: QueuedReport): Promise<'dropped'> {
    await this.remove(report.id);
    this.toast.error(this.i18n.t('queue.rejected'));
    return 'dropped';
  }

  private async update(report: QueuedReport): Promise<void> {
    await this.storage.put(report);
    this.queue.update(list => list.map(item => (item.id === report.id ? report : item)));
  }

  private async remove(id: string): Promise<void> {
    await this.storage.remove(id);
    this.queue.update(list => list.filter(item => item.id !== id));
  }
}
