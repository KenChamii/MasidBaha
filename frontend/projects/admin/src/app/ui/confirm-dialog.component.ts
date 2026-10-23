import { ChangeDetectionStrategy, Component, HostListener, inject } from '@angular/core';
import { ConfirmService } from './confirm.service';

// Render once at the root: <app-confirm-dialog />
@Component({
  selector: 'app-confirm-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (confirm.pending(); as request) {
      <div class="dialog-backdrop" role="presentation" (click)="request.resolve(false)">
        <div
          class="dialog card"
          role="alertdialog"
          aria-modal="true"
          aria-labelledby="confirm-title"
          aria-describedby="confirm-message"
          (click)="$event.stopPropagation()"
          (keydown.escape)="request.resolve(false)"
        >
          <h2 id="confirm-title">{{ request.title }}</h2>
          <p id="confirm-message">{{ request.message }}</p>
          <div class="dialog__actions">
            <button type="button" class="btn btn--sm btn--ghost" (click)="request.resolve(false)">Cancel</button>
            <button
              type="button"
              class="btn btn--sm"
              [class.btn--danger]="request.danger"
              [class.btn--primary]="!request.danger"
              (click)="request.resolve(true)"
            >
              {{ request.confirmLabel ?? 'Confirm' }}
            </button>
          </div>
        </div>
      </div>
    }
  `
})
export class ConfirmDialogComponent {
  protected readonly confirm = inject(ConfirmService);

  @HostListener('document:keydown.escape')
  protected onEscape(): void {
    this.confirm.pending()?.resolve(false);
  }
}
