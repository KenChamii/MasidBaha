import { ChangeDetectionStrategy, Component, OnDestroy, inject, input, output, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import {
  FloodReportService,
  I18nService,
  PhotoService,
  SEVERITY_OPTIONS,
  Severity,
  ToastService,
  TranslatePipe
} from '@masidbaha/shared';
import { ReportQueueService, isRetryable, newRequestId } from '../../../../core/services/report-queue.service';

const MAX_PHOTO_BYTES = 8 * 1024 * 1024;
const ALLOWED_PHOTO_TYPES = ['image/jpeg', 'image/png', 'image/webp'];

@Component({
  selector: 'app-report-form',
  imports: [FormsModule, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './report-form.component.html',
  styleUrl: './report-form.component.scss'
})
export class ReportFormComponent implements OnDestroy {
  private readonly floodReportService = inject(FloodReportService);
  private readonly photoService = inject(PhotoService);
  private readonly queue = inject(ReportQueueService);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);

  // This report's own id: sent with every attempt, so the server saves it once however many times it is sent
  private readonly requestId = newRequestId();

  readonly pin = input.required<{ lat: number; lng: number }>();
  readonly submitted = output<void>();
  readonly cancelled = output<void>();

  readonly severityOptions = SEVERITY_OPTIONS;
  severity: Severity = 1;
  notes = '';

  readonly isSubmitting = signal(false);
  readonly isUploadingPhoto = signal(false);
  readonly photoPreviewUrl = signal<string | null>(null);
  // a message key (see core/i18n/messages.ts), so it follows the language if that changes
  readonly photoError = signal<string | null>(null);
  private selectedPhotoFile: File | null = null;

  ngOnDestroy(): void {
    this.clearPhoto();
  }

  // Client-side guard mirrors the backend's own checks so people get
  // instant feedback instead of waiting on a round trip.
  onPhotoSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    this.photoError.set(null);

    if (!file) {
      this.clearPhoto();
      return;
    }

    if (!ALLOWED_PHOTO_TYPES.includes(file.type)) {
      this.photoError.set('form.err.type');
      input.value = '';
      return;
    }

    if (file.size > MAX_PHOTO_BYTES) {
      this.photoError.set('form.err.size');
      input.value = '';
      return;
    }

    this.revokePreview();
    this.selectedPhotoFile = file;
    this.photoPreviewUrl.set(URL.createObjectURL(file));
  }

  clearPhoto(): void {
    this.revokePreview();
    this.selectedPhotoFile = null;
    this.photoPreviewUrl.set(null);
    this.photoError.set(null);
  }

  submit(): void {
    if (this.isSubmitting()) return;
    this.isSubmitting.set(true);

    // No connection at all: keep the report on the device and send it later
    if (navigator.onLine === false) {
      void this.saveForLater();
      return;
    }

    // Photo is optional: upload it first (if present) to get a URL, then create the report
    if (!this.selectedPhotoFile) {
      this.createReport(undefined);
      return;
    }

    this.isUploadingPhoto.set(true);
    (this.photoService.upload(this.selectedPhotoFile, { quiet: true }) as Observable<{ url: string }>).subscribe({
      next: result => {
        this.isUploadingPhoto.set(false);
        this.createReport(result.url);
      },
      error: error => {
        this.isUploadingPhoto.set(false);
        // The connection dropped on the way: keep the whole report (with its photo) and send it later
        if (isRetryable(error)) {
          void this.saveForLater();
          return;
        }
        this.isSubmitting.set(false);
        this.photoError.set('form.err.upload');
      }
    });
  }

  private createReport(photoUrl: string | undefined): void {
    this.floodReportService
      .create(
        {
          lat: this.pin().lat,
          lng: this.pin().lng,
          severity: this.severity,
          notes: this.notes || undefined,
          photoUrl,
          clientRequestId: this.requestId
        },
        { quiet: true }
      )
      .subscribe({
        // The new marker arrives through SignalR, so there's nothing to add by hand here
        next: () => {
          this.isSubmitting.set(false);
          this.submitted.emit();
        },
        error: (error: HttpErrorResponse) => {
          // The answer may have been lost on the way back, which is why the retry carries the same id
          if (isRetryable(error) && error.status !== 429) {
            void this.saveForLater();
            return;
          }
          this.isSubmitting.set(false);
          this.showFailure(error);
        }
      });
  }

  // Keeps the report on the device; it is sent when the connection is back
  private async saveForLater(): Promise<void> {
    const saved = await this.queue.enqueue({
      id: this.requestId,
      lat: this.pin().lat,
      lng: this.pin().lng,
      severity: this.severity,
      notes: this.notes || undefined,
      photo: this.selectedPhotoFile ?? undefined
    });

    this.isSubmitting.set(false);
    if (!saved) {
      this.toast.error(this.i18n.t('queue.full'));
      return;
    }
    this.toast.show(this.i18n.t('queue.saved'), 'info');
    this.submitted.emit();
    // Try straight away: the failure may have been a hiccup
    void this.queue.flush();
  }

  // The reasons for a failure that waiting will not fix are shown where the person is looking
  private showFailure(error: HttpErrorResponse): void {
    const detail = Object.values((error.error?.errors as Record<string, string[]> | undefined) ?? {}).flat()[0];
    this.toast.error(detail ?? error.error?.detail ?? this.i18n.t(error.status === 429 ? 'err.tooMany' : 'err.server'));
  }

  private revokePreview(): void {
    const url = this.photoPreviewUrl();
    if (url) URL.revokeObjectURL(url);
  }
}
