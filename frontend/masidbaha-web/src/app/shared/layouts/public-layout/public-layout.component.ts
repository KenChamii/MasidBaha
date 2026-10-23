import { Component, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { BrandComponent } from '../../ui/brand/brand.component';
import { ThemeToggleComponent } from '../../ui/theme-toggle/theme-toggle.component';

@Component({
    selector: 'app-public-layout',
    imports: [RouterOutlet, RouterLink, BrandComponent, ThemeToggleComponent],
    templateUrl: './public-layout.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    styleUrl: './public-layout.component.scss'
})
export class PublicLayoutComponent {
  readonly currentYear = new Date().getFullYear();
}
