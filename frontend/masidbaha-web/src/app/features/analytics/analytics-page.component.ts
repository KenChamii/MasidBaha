import { AfterViewInit, Component, ElementRef, OnDestroy, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import * as L from 'leaflet';
import 'leaflet.heat';

import { FloodReportService } from '../map/services/flood-report.service';
import { HeatmapPoint } from '../../shared/models/flood-report.model';

const PHILIPPINES_CENTER: L.LatLngExpression = [12.8797, 121.7740];
const DEFAULT_ZOOM = 6;

type RangeOption = '1m' | '3m' | '6m' | '1y' | 'all';

const RANGE_MONTHS: Record<RangeOption, number | null> = {
  '1m': 1,
  '3m': 3,
  '6m': 6,
  '1y': 12,
  'all': null
};

// Severity is the weighting signal for the heat layer — worse floods
// contribute more "heat" per point, so a single Impassable report can
// outweigh several Passable ones in the same area.
const SEVERITY_WEIGHT: Record<number, number> = {
  1: 0.4,
  2: 0.6,
  3: 0.8,
  4: 1.0
};

@Component({
  selector: 'app-analytics-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './analytics-page.component.html',
  styleUrls: ['./analytics-page.component.scss']
})
export class AnalyticsPageComponent implements AfterViewInit, OnDestroy {
  @ViewChild('mapContainer', { static: true }) mapContainerRef!: ElementRef<HTMLDivElement>;

  private map!: L.Map;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  private heatLayer: any;

  range: RangeOption = '6m';
  isLoading = false;
  points: HeatmapPoint[] = [];

  private filterSample: HeatmapPoint[] = [];
  selectedRegion: string | null = null;
  selectedProvince: string | null = null;
  selectedCity: string | null = null;

  get availableRegions(): string[] {
    return this.distinct(this.filterSample.map(p => p.region));
  }

  get availableProvinces(): string[] {
    return this.distinct(
      this.filterSample
        .filter(p => !this.selectedRegion || p.region === this.selectedRegion)
        .map(p => p.province)
    );
  }

  get availableCities(): string[] {
    return this.distinct(
      this.filterSample
        .filter(p =>
          (!this.selectedRegion || p.region === this.selectedRegion) &&
          (!this.selectedProvince || p.province === this.selectedProvince)
        )
        .map(p => p.city)
    );
  }

  private distinct(values: (string | undefined)[]): string[] {
    return Array.from(new Set(values.filter((v): v is string => !!v))).sort();
  }

  constructor(private floodReportService: FloodReportService) {}

  ngAfterViewInit(): void {
    this.map = L.map(this.mapContainerRef.nativeElement).setView(PHILIPPINES_CENTER, DEFAULT_ZOOM);

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '&copy; OpenStreetMap contributors',
      maxZoom: 19
    }).addTo(this.map);

    this.loadHeatmap();
  }

  ngOnDestroy(): void {
    this.map?.remove();
  }

  setRange(range: RangeOption): void {
    this.range = range;
    this.loadHeatmap();
  }

  onRegionChange(region: string): void {
    this.selectedRegion = region || null;
    this.selectedProvince = null;
    this.selectedCity = null;
    this.loadHeatmap();
  }

  onProvinceChange(province: string): void {
    this.selectedProvince = province || null;
    this.selectedCity = null;
    this.loadHeatmap();
  }

  onCityChange(city: string): void {
    this.selectedCity = city || null;
    this.loadHeatmap();
  }

  private loadHeatmap(): void {
    this.isLoading = true;

    const months = RANGE_MONTHS[this.range];
    const fromDate = months
      ? new Date(Date.now() - months * 30 * 24 * 60 * 60 * 1000).toISOString()
      : undefined;

    this.floodReportService.getHeatmap({
      fromDate,
      region: this.selectedRegion ?? undefined,
      province: this.selectedProvince ?? undefined,
      city: this.selectedCity ?? undefined
    }).subscribe({
      next: points => {
        this.points = points;
        // Filter dropdowns are populated from an unfiltered sample so options
        // don't disappear as soon as you pick a region — refreshed only when
        // there's no active filter yet, mirroring the map page's pattern.
        if (!this.selectedRegion && !this.selectedProvince && !this.selectedCity) {
          this.filterSample = points;
        }
        this.renderHeatLayer(points);
        this.fitToPoints(points);
        this.isLoading = false;
      },
      error: () => this.isLoading = false
    });
  }

  // Without this, the map stays parked on the Philippines-wide default view
  // even when the actual historical points are clustered somewhere small
  // (e.g. a handful of test reports in one city) — making the heat layer
  // technically present but invisible/unnoticeable at that zoom level.
  private fitToPoints(points: HeatmapPoint[]): void {
    if (points.length === 0) return;

    console.log('[analytics] heatmap points:', points.map(p => ({ lat: p.lat, lng: p.lng, severity: p.severity })));

    const bounds = L.latLngBounds(points.map(p => [p.lat, p.lng] as L.LatLngTuple));
    this.map.fitBounds(bounds, { padding: [60, 60], maxZoom: 13 });
  }

  private renderHeatLayer(points: HeatmapPoint[]): void {
    if (this.heatLayer) {
      this.map.removeLayer(this.heatLayer);
    }

    const heatPoints: [number, number, number][] = points.map(p => [
      p.lat,
      p.lng,
      SEVERITY_WEIGHT[p.severity] ?? 0.5
    ]);

    // NOTE: we intentionally read heatLayer off `window.L` here rather than
    // the module-scoped `L` import. Under esbuild (Angular's default
    // builder), `import * as L from 'leaflet'` produces a namespace object
    // that doesn't reflect properties the leaflet.heat UMD plugin attaches
    // to the *global* L (set up in main.ts) — so `L.heatLayer` stays
    // undefined even though the plugin successfully patched window.L.
    const globalL = (window as any).L;
    if (!globalL?.heatLayer) {
      console.error('leaflet.heat did not attach to window.L — heatmap cannot render.');
      return;
    }

    this.heatLayer = globalL.heatLayer(heatPoints, {
      radius: 45,
      blur: 30,
      minOpacity: 0.45,
      max: 0.6,
      maxZoom: 14,
      gradient: { 0.2: '#34C6E8', 0.4: '#FFD166', 0.7: '#FF7A47', 1.0: '#E63946' }
    }).addTo(this.map);
  }
}
