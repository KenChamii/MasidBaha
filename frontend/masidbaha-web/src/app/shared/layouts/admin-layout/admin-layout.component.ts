import { Component, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { BrandComponent } from '../../ui/brand/brand.component';
import { ThemeToggleComponent } from '../../ui/theme-toggle/theme-toggle.component';

@Component({
    selector: 'app-admin-layout',
    imports: [RouterOutlet, RouterLink, BrandComponent, ThemeToggleComponent],
    templateUrl: './admin-layout.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    styleUrl: './admin-layout.component.scss'
})
export class AdminLayoutComponent {}
