import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export interface BarItem {
  label: string;
  value: number;
  // Falls back to the brand colour
  color?: string;
}

// Horizontal bars with the label and value written out, so colour is never the only carrier of meaning
@Component({
  selector: 'app-bar-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ul class="bars">
      @for (item of items(); track item.label) {
        <li [title]="item.label + ': ' + item.value">
          <span class="bars__label">{{ item.label }}</span>
          <span class="bars__track">
            <span
              class="bars__fill"
              [style.width.%]="(item.value / max()) * 100"
              [style.background]="item.color ?? 'var(--mb-water)'"
            ></span>
          </span>
          <span class="bars__value">{{ item.value }}</span>
        </li>
      } @empty {
        <li class="bars__empty">No data yet.</li>
      }
    </ul>
  `,
  styles: `
    .bars {
      list-style: none;
      margin: 0;
      padding: 0;
      display: grid;
      gap: 10px;
    }
    li {
      display: grid;
      grid-template-columns: 110px 1fr 36px;
      align-items: center;
      gap: 10px;
      font-size: 0.85rem;
    }
    .bars__label {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .bars__track {
      height: 12px;
    }
    .bars__fill {
      display: block;
      height: 100%;
      min-width: 2px;
      border-radius: 0 4px 4px 0;
    }
    .bars__value {
      text-align: right;
      font-variant-numeric: tabular-nums;
    }
    .bars__empty {
      display: block;
      color: var(--mb-muted);
    }
  `
})
export class BarListComponent {
  readonly items = input.required<BarItem[]>();

  protected readonly max = computed(() => Math.max(1, ...this.items().map(i => i.value)));
}
