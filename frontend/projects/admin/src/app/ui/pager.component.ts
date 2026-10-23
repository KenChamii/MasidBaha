import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

// Previous / next controls under every paged table
@Component({
  selector: 'app-pager',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (totalCount() > 0) {
      <nav class="pager" aria-label="Pagination">
        <button type="button" class="btn btn--sm" [disabled]="page() <= 1" (click)="pageChange.emit(page() - 1)">
          ← Prev
        </button>
        <span class="pager__info">Page {{ page() }} of {{ totalPages() }} · {{ totalCount() }} total</span>
        <button
          type="button"
          class="btn btn--sm"
          [disabled]="page() >= totalPages()"
          (click)="pageChange.emit(page() + 1)"
        >
          Next →
        </button>
      </nav>
    }
  `
})
export class PagerComponent {
  readonly page = input.required<number>();
  readonly pageSize = input.required<number>();
  readonly totalCount = input.required<number>();
  readonly pageChange = output<number>();

  protected readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.pageSize())));
}
