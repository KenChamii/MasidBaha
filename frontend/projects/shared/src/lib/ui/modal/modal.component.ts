import { ChangeDetectionStrategy, Component, HostListener, output } from '@angular/core';

@Component({
  selector: 'mb-modal',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="modal"
      role="dialog"
      aria-modal="true"
      aria-label="Dialog"
      tabindex="-1"
      (click)="closed.emit()"
      (keydown.escape)="closed.emit()"
    >
      <button type="button" class="modal__close" aria-label="Close" (click)="closed.emit()">✕</button>
      <div role="presentation" (click)="$event.stopPropagation()">
        <ng-content />
      </div>
    </div>
  `,
  styleUrl: './modal.component.scss'
})
export class ModalComponent {
  readonly closed = output<void>();

  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.closed.emit();
  }
}
