import { Injectable, signal } from '@angular/core';

export type ToastKind = 'error' | 'info' | 'success';

export interface Toast {
  id: number;
  kind: ToastKind;
  message: string;
}

// Short messages that fade on their own: used for failures the user can't act
// on inside a form (server down, rate limited), so they aren't left guessing.
@Injectable({ providedIn: 'root' })
export class ToastService {
  private nextId = 1;

  readonly toasts = signal<Toast[]>([]);

  show(message: string, kind: ToastKind = 'info', durationMs = 6000): void {
    // The same message is not stacked twice (e.g. several requests failing together)
    if (this.toasts().some(t => t.message === message)) return;

    const toast: Toast = { id: this.nextId++, kind, message };
    this.toasts.update(list => [...list, toast]);

    if (durationMs > 0) {
      setTimeout(() => this.dismiss(toast.id), durationMs);
    }
  }

  error(message: string): void {
    this.show(message, 'error');
  }

  success(message: string): void {
    this.show(message, 'success', 4000);
  }

  dismiss(id: number): void {
    this.toasts.update(list => list.filter(t => t.id !== id));
  }
}
