import { ChangeDetectionStrategy, Component, OnDestroy, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { FloodReportService, PhotoService, SEVERITY_OPTIONS, Severity, TranslatePipe } from '@masidbaha/shared';

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

    // Photo is optional: upload it first (if present) to get a URL, then create the report
    if (!this.selectedPhotoFile) {
      this.createReport(undefined);
      return;
    }

    this.isUploadingPhoto.set(true);
    (this.photoService.upload(this.selectedPhotoFile) as Observable<{ url: string }>).subscribe({
      next: result => {
        this.isUploadingPhoto.set(false);
        this.createReport(result.url);
      },
      error: () => {
        this.isUploadingPhoto.set(false);
        this.isSubmitting.set(false);
        this.photoError.set('form.err.upload');
      }
    });
  }

  private createReport(photoUrl: string | undefined): void {
    this.floodReportService
      .create({
        lat: this.pin().lat,
        lng: this.pin().lng,
        severity: this.severity,
        notes: this.notes || undefined,
        photoUrl
      })
      .subscribe({
        // The new marker arrives through SignalR, so there's nothing to add by hand here
        next: () => {
          this.isSubmitting.set(false);
          this.submitted.emit();
        },
        error: () => this.isSubmitting.set(false)
      });
  }

  private revokePreview(): void {
    const url = this.photoPreviewUrl();
    if (url) URL.revokeObjectURL(url);
  }
}
