import { ChangeDetectionStrategy, Component, HostListener, output } from '@angular/core';

@Component({
  selector: 'app-modal',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="modal" role="dialog" aria-modal="true" [attr.aria-label]="'Dialog'" (click)="closed.emit()">
      <button type="button" class="modal__close" aria-label="Close" (click)="closed.emit()">✕</button>
      <div (click)="$event.stopPropagation()">
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
