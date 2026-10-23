import { ChangeDetectionStrategy, Component, input } from '@angular/core';

// A table row of grey placeholders, for a table that is still loading: <tr appSkeletonRow [columns]="5"></tr>
@Component({
  // a row component has to BE the <tr>: a custom element inside <tbody> is not valid table markup
  // eslint-disable-next-line @angular-eslint/component-selector
  selector: 'tr[appSkeletonRow]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (cell of cells(); track cell) {
      <td aria-hidden="true"><span class="skeleton skeleton-cell"></span></td>
    }
  `,
  styles: `
    .skeleton-cell {
      height: 14px;
      width: 80%;
    }
  `
})
export class SkeletonRowComponent {
  readonly columns = input.required<number>();

  protected cells(): number[] {
    return Array.from({ length: this.columns() }, (_, index) => index);
  }
}
