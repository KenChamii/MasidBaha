import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ToastService } from './toast.service';

// Render once at the root of an app: <mb-toast-container />
@Component({
  selector: 'mb-toast-container',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="toasts" aria-live="polite">
      @for (toast of toastService.toasts(); track toast.id) {
        <div
          class="toast"
          [class]="'toast toast--' + toast.kind"
          [attr.role]="toast.kind === 'error' ? 'alert' : 'status'"
        >
          <span class="toast__message">{{ toast.message }}</span>
          <button type="button" class="toast__close" aria-label="Dismiss" (click)="toastService.dismiss(toast.id)">
            ✕
          </button>
        </div>
      }
    </div>
  `,
  styleUrl: './toast-container.component.scss'
})
export class ToastContainerComponent {
  readonly toastService = inject(ToastService);
}
