import { Component } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { BrandComponent, ThemeToggleComponent } from '@masidbaha/shared';

@Component({
  selector: 'app-public-layout',
  imports: [RouterOutlet, RouterLink, BrandComponent, ThemeToggleComponent],
  templateUrl: './public-layout.component.html',
  styleUrl: './public-layout.component.scss'
})
export class PublicLayoutComponent {
  readonly currentYear = new Date().getFullYear();
}
