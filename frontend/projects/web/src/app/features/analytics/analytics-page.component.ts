import { AfterViewInit, Component, ElementRef, OnDestroy, computed, inject, signal, viewChild } from '@angular/core';
import { FloodReportService, HeatmapPoint } from '@masidbaha/shared';
import * as L from 'leaflet';
import 'leaflet.heat';

const PHILIPPINES_CENTER: L.LatLngExpression = [12.8797, 121.774];
const DEFAULT_ZOOM = 6;

type RangeOption = '1m' | '3m' | '6m' | '1y' | 'all';

const RANGE_MONTHS: Record<RangeOption, number | null> = {
  '1m': 1,
  '3m': 3,
  '6m': 6,
  '1y': 12,
  all: null
};

// Severity is the weighting signal for the heat layer: worse floods
// contribute more "heat" per point, so a single Impassable report can
// outweigh several Passable ones in the same area.
const SEVERITY_WEIGHT: Record<number, number> = {
  1: 0.4,
  2: 0.6,
  3: 0.8,
  4: 1.0
};

// leaflet.heat attaches heatLayer to the global L, which has no type declaration
type HeatLayerFactory = (points: [number, number, number][], options: Record<string, unknown>) => L.Layer;

@Component({
  selector: 'app-analytics-page',
  templateUrl: './analytics-page.component.html',
  styleUrls: ['./analytics-page.component.scss']
})
export class AnalyticsPageComponent implements AfterViewInit, OnDestroy {
  private readonly floodReportService = inject(FloodReportService);
  private readonly mapContainer = viewChild.required<ElementRef<HTMLDivElement>>('mapContainer');

  private map!: L.Map;
  private heatLayer?: L.Layer;

  protected readonly range = signal<RangeOption>('6m');
  protected readonly isLoading = signal(false);
  protected readonly points = signal<HeatmapPoint[]>([]);

  protected readonly selectedRegion = signal<string | null>(null);
  protected readonly selectedProvince = signal<string | null>(null);
  protected readonly selectedCity = signal<string | null>(null);

  // Dropdown options come from an unfiltered sample, so they don't disappear
  // as soon as a region is picked
  private readonly filterSample = signal<HeatmapPoint[]>([]);

  protected readonly availableRegions = computed(() => this.distinct(this.filterSample().map(p => p.region)));

  protected readonly availableProvinces = computed(() => {
    const region = this.selectedRegion();
    return this.distinct(
      this.filterSample()
        .filter(p => !region || p.region === region)
        .map(p => p.province)
    );
  });

  protected readonly availableCities = computed(() => {
    const region = this.selectedRegion();
    const province = this.selectedProvince();
    return this.distinct(
      this.filterSample()
        .filter(p => (!region || p.region === region) && (!province || p.province === province))
        .map(p => p.city)
    );
  });

  ngAfterViewInit(): void {
    this.map = L.map(this.mapContainer().nativeElement).setView(PHILIPPINES_CENTER, DEFAULT_ZOOM);

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '&copy; OpenStreetMap contributors',
      maxZoom: 19
    }).addTo(this.map);

    this.loadHeatmap();
  }

  ngOnDestroy(): void {
    this.map?.remove();
  }

  protected setRange(range: RangeOption): void {
    this.range.set(range);
    this.loadHeatmap();
  }

  protected onRegionChange(event: Event): void {
    this.selectedRegion.set(this.valueOf(event) || null);
    this.selectedProvince.set(null);
    this.selectedCity.set(null);
    this.loadHeatmap();
  }

  protected onProvinceChange(event: Event): void {
    this.selectedProvince.set(this.valueOf(event) || null);
    this.selectedCity.set(null);
    this.loadHeatmap();
  }

  protected onCityChange(event: Event): void {
    this.selectedCity.set(this.valueOf(event) || null);
    this.loadHeatmap();
  }

  private valueOf(event: Event): string {
    return (event.target as HTMLSelectElement).value;
  }

  private distinct(values: (string | undefined)[]): string[] {
    return Array.from(new Set(values.filter((v): v is string => !!v))).sort();
  }

  private loadHeatmap(): void {
    this.isLoading.set(true);

    const months = RANGE_MONTHS[this.range()];
    const fromDate = months ? new Date(Date.now() - months * 30 * 24 * 60 * 60 * 1000).toISOString() : undefined;

    this.floodReportService
      .getHeatmap({
        fromDate,
        region: this.selectedRegion() ?? undefined,
        province: this.selectedProvince() ?? undefined,
        city: this.selectedCity() ?? undefined
      })
      .subscribe({
        next: points => {
          this.points.set(points);
          // Refreshed only while no filter is active, so the options stay stable
          if (!this.selectedRegion() && !this.selectedProvince() && !this.selectedCity()) {
            this.filterSample.set(points);
          }
          this.renderHeatLayer(points);
          this.fitToPoints(points);
          this.isLoading.set(false);
        },
        error: () => this.isLoading.set(false)
      });
  }

  // Without this, the map stays parked on the Philippines-wide default view
  // even when the actual historical points are clustered somewhere small
  // (e.g. a handful of reports in one city), which would make the heat layer
  // technically present but unnoticeable at that zoom level.
  private fitToPoints(points: HeatmapPoint[]): void {
    if (points.length === 0) return;

    const bounds = L.latLngBounds(points.map(p => [p.lat, p.lng] as L.LatLngTuple));
    this.map.fitBounds(bounds, { padding: [60, 60], maxZoom: 13 });
  }

  private renderHeatLayer(points: HeatmapPoint[]): void {
    if (this.heatLayer) {
      this.map.removeLayer(this.heatLayer);
    }

    const heatPoints: [number, number, number][] = points.map(p => [p.lat, p.lng, SEVERITY_WEIGHT[p.severity] ?? 0.5]);

    // We read heatLayer off `window.L` on purpose, not the module-scoped `L`.
    // Under esbuild, `import * as L from 'leaflet'` gives a namespace object
    // that doesn't reflect properties the leaflet.heat UMD plugin attaches to
    // the *global* L (set up in main.ts), so `L.heatLayer` would stay undefined.
    const globalL = (window as unknown as { L?: { heatLayer?: HeatLayerFactory } }).L;
    if (!globalL?.heatLayer) {
      console.error('leaflet.heat did not attach to window.L, so the heatmap cannot render.');
      return;
    }

    this.heatLayer = globalL
      .heatLayer(heatPoints, {
        radius: 45,
        blur: 30,
        minOpacity: 0.45,
        max: 0.6,
        maxZoom: 14,
        gradient: { 0.2: '#34C6E8', 0.4: '#FFD166', 0.7: '#FF7A47', 1.0: '#E63946' }
      })
      .addTo(this.map);
  }
}
