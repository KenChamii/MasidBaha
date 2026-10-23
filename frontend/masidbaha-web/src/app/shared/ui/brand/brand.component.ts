import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
    selector: 'app-brand',
    imports: [RouterLink],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
    <a class="brand" routerLink="/" aria-label="MasidBaha home">
      <span class="brand__logo">
        <svg viewBox="0 0 24 24" width="18" height="18" fill="none" aria-hidden="true">
          <path d="M4 15c2-4 4-6 8-6s6 2 8 6" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
          <path d="M4 19c2-3 4-4.5 8-4.5s6 1.5 8 4.5" stroke="currentColor" stroke-width="2" stroke-linecap="round" opacity="0.5"/>
        </svg>
      </span>
      <span class="brand__name">MasidBaha</span>
    </a>
  `,
    styleUrl: './brand.component.scss'
})
export class BrandComponent {}
