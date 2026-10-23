import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ThemeService, ToastContainerComponent } from '@masidbaha/shared';
import { ConfirmDialogComponent } from './ui/confirm-dialog.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ToastContainerComponent, ConfirmDialogComponent],
  template: `
    <router-outlet />
    <mb-toast-container />
    <app-confirm-dialog />
  `
})
export class AppComponent {
  // Instantiating the service applies the saved theme on startup
  private readonly theme = inject(ThemeService);
}
