import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ThemeService, ToastContainerComponent } from '@masidbaha/shared';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, ToastContainerComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent {
  // Instantiating the service applies the saved theme on startup
  private readonly theme = inject(ThemeService);
}
