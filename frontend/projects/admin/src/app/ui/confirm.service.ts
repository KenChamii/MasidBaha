import { Injectable, signal } from '@angular/core';

export interface ConfirmOptions {
  title: string;
  message: string;
  confirmLabel?: string;
  // Styles the confirm button as destructive
  danger?: boolean;
}

export interface ConfirmRequest extends ConfirmOptions {
  resolve: (confirmed: boolean) => void;
}

// An in-page replacement for window.confirm(): `if (await confirm.ask({...})) { ... }`
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  // The dialog currently on screen (rendered once by ConfirmDialogComponent)
  readonly pending = signal<ConfirmRequest | null>(null);

  ask(options: ConfirmOptions): Promise<boolean> {
    // A second question while one is open cancels the first, so nothing is left hanging
    this.pending()?.resolve(false);

    return new Promise<boolean>(resolve => {
      this.pending.set({
        ...options,
        resolve: confirmed => {
          this.pending.set(null);
          resolve(confirmed);
        }
      });
    });
  }
}
