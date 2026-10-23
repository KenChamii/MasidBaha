import { Component, inject } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import {
  BrandComponent,
  I18nService,
  LangToggleComponent,
  ThemeToggleComponent,
  TranslatePipe
} from '@masidbaha/shared';

@Component({
  selector: 'app-public-layout',
  imports: [RouterOutlet, RouterLink, BrandComponent, LangToggleComponent, ThemeToggleComponent, TranslatePipe],
  templateUrl: './public-layout.component.html',
  styleUrl: './public-layout.component.scss'
})
export class PublicLayoutComponent {
  protected readonly i18n = inject(I18nService);
  readonly currentYear = new Date().getFullYear();
}
