import { Component } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { BrandComponent } from '../../ui/brand/brand.component';
import { ThemeToggleComponent } from '../../ui/theme-toggle/theme-toggle.component';

@Component({
  selector: 'app-admin-layout',
  standalone: true,
  imports: [RouterOutlet, RouterLink, BrandComponent, ThemeToggleComponent],
  templateUrl: './admin-layout.component.html',
  styleUrl: './admin-layout.component.scss'
})
export class AdminLayoutComponent {}
