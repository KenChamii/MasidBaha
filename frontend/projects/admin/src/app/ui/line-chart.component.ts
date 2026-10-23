import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';

export interface ChartPoint {
  label: string;
  value: number;
}

const WIDTH = 1000;
const HEIGHT = 240;
const PAD = { top: 12, right: 12, bottom: 26, left: 34 };

// One series over time: a thin line, a recessive grid, a crosshair with a tooltip on hover
@Component({
  selector: 'app-line-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="chart">
      <svg
        [attr.viewBox]="'0 0 ' + width + ' ' + height"
        role="img"
        [attr.aria-label]="ariaLabel()"
        (mousemove)="onMove($event)"
        (mouseleave)="hovered.set(null)"
      >
        @for (tick of yTicks(); track tick.value) {
          <line class="grid" [attr.x1]="pad.left" [attr.x2]="width - pad.right" [attr.y1]="tick.y" [attr.y2]="tick.y" />
          <text class="axis" [attr.x]="pad.left - 6" [attr.y]="tick.y + 4" text-anchor="end">{{ tick.value }}</text>
        }
        @for (tick of xTicks(); track tick.label) {
          <text class="axis" [attr.x]="tick.x" [attr.y]="height - 6" [attr.text-anchor]="tick.anchor">
            {{ tick.label }}
          </text>
        }

        <path class="area" [attr.d]="areaPath()" />
        <path class="line" [attr.d]="linePath()" />

        @if (hoveredPoint(); as p) {
          <line class="cross" [attr.x1]="p.x" [attr.x2]="p.x" [attr.y1]="pad.top" [attr.y2]="height - pad.bottom" />
          <circle class="dot" [attr.cx]="p.x" [attr.cy]="p.y" r="4.5" />
        }
      </svg>

      @if (hoveredPoint(); as p) {
        <div class="tip" [style.left.%]="(p.x / width) * 100">
          <strong>{{ p.value }}</strong> {{ unit() }}<br />{{ p.label }}
        </div>
      }
    </div>
  `,
  styles: `
    .chart {
      position: relative;
    }
    svg {
      display: block;
      width: 100%;
      height: auto;
      overflow: visible;
    }
    .grid {
      stroke: var(--mb-ink);
      stroke-opacity: 0.12;
    }
    .axis {
      fill: var(--mb-muted);
      font: 11px var(--mb-font-mono);
    }
    .line {
      fill: none;
      stroke: var(--mb-water-text);
      stroke-width: 2;
      stroke-linejoin: round;
    }
    .area {
      fill: var(--mb-water-text);
      fill-opacity: 0.1;
    }
    .cross {
      stroke: var(--mb-ink);
      stroke-opacity: 0.4;
      stroke-dasharray: 3 3;
    }
    .dot {
      fill: var(--mb-water-text);
      stroke: var(--mb-surface);
      stroke-width: 2;
    }
    .tip {
      position: absolute;
      top: 0;
      transform: translateX(-50%);
      background: var(--mb-ink);
      color: var(--mb-on-ink);
      padding: 4px 8px;
      font-size: 0.75rem;
      line-height: 1.3;
      pointer-events: none;
      white-space: nowrap;
    }
  `
})
export class LineChartComponent {
  readonly points = input.required<ChartPoint[]>();
  readonly unit = input('reports');
  readonly ariaLabel = input('Line chart');

  protected readonly width = WIDTH;
  protected readonly height = HEIGHT;
  protected readonly pad = PAD;
  protected readonly hovered = signal<number | null>(null);

  private readonly max = computed(() => {
    const top = Math.max(1, ...this.points().map(p => p.value));
    // Round up to a tidy ceiling so the gridlines carry whole numbers
    const step = top <= 4 ? 1 : Math.pow(10, Math.floor(Math.log10(top))) / (top < 20 ? 2 : 1);
    return Math.ceil(top / step) * step;
  });

  private readonly plotted = computed(() => {
    const pts = this.points();
    const innerW = WIDTH - PAD.left - PAD.right;
    const innerH = HEIGHT - PAD.top - PAD.bottom;
    return pts.map((p, i) => ({
      ...p,
      x: PAD.left + (pts.length > 1 ? (i / (pts.length - 1)) * innerW : innerW / 2),
      y: PAD.top + innerH - (p.value / this.max()) * innerH
    }));
  });

  protected readonly linePath = computed(() =>
    this.plotted()
      .map((p, i) => `${i ? 'L' : 'M'}${p.x.toFixed(1)},${p.y.toFixed(1)}`)
      .join('')
  );

  protected readonly areaPath = computed(() => {
    const pts = this.plotted();
    if (pts.length === 0) return '';
    const base = HEIGHT - PAD.bottom;
    return `${this.linePath()}L${pts[pts.length - 1].x.toFixed(1)},${base}L${pts[0].x.toFixed(1)},${base}Z`;
  });

  protected readonly yTicks = computed(() => {
    const innerH = HEIGHT - PAD.top - PAD.bottom;
    return [0, 0.5, 1].map(f => ({ value: Math.round(this.max() * f), y: PAD.top + innerH - f * innerH }));
  });

  protected readonly xTicks = computed(() => {
    const pts = this.plotted();
    if (pts.length === 0) return [];
    const mid = pts[Math.floor(pts.length / 2)];
    return [
      { label: pts[0].label, x: pts[0].x, anchor: 'start' },
      { label: mid.label, x: mid.x, anchor: 'middle' },
      { label: pts[pts.length - 1].label, x: pts[pts.length - 1].x, anchor: 'end' }
    ];
  });

  protected readonly hoveredPoint = computed(() => {
    const index = this.hovered();
    return index === null ? null : (this.plotted()[index] ?? null);
  });

  protected onMove(event: MouseEvent): void {
    const pts = this.plotted();
    if (pts.length === 0) return;

    const svg = event.currentTarget as SVGSVGElement;
    const rect = svg.getBoundingClientRect();
    // Mouse position in the chart's own coordinates, then the nearest point
    const x = ((event.clientX - rect.left) / rect.width) * WIDTH;
    let best = 0;
    for (let i = 1; i < pts.length; i++) {
      if (Math.abs(pts[i].x - x) < Math.abs(pts[best].x - x)) best = i;
    }
    this.hovered.set(best);
  }
}
