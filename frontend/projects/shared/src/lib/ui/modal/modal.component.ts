import { ChangeDetectionStrategy, Component, HostListener, input, output } from '@angular/core';

@Component({
  selector: 'mb-modal',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="modal"
      [class.modal--sheet]="sheet()"
      role="dialog"
      aria-modal="true"
      aria-label="Dialog"
      tabindex="-1"
      (click)="closed.emit()"
      (keydown.escape)="closed.emit()"
    >
      <button type="button" class="modal__close" aria-label="Close" (click)="closed.emit()">✕</button>
      <div class="modal__body" role="presentation" (click)="$event.stopPropagation()">
        <ng-content />
      </div>
    </div>
  `,
  styleUrl: './modal.component.scss'
})
export class ModalComponent {
  readonly closed = output<void>();

  // Panels (settings, filters) rise from the bottom on phones; a photo stays centred
  readonly sheet = input(false);

  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.closed.emit();
  }
}
