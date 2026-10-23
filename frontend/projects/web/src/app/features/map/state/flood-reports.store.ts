import { Injectable, computed, inject, signal } from '@angular/core';
import { FloodReport, FloodReportService, ReportScope } from '@masidbaha/shared';

const FILTER_SAMPLE_LIMIT = 200;
const TOP_REPORTS_LIMIT = 20;

// Top-reports panel state. Provided per map page so it dies with the page.
@Injectable()
export class FloodReportsStore {
  private readonly api = inject(FloodReportService);

  readonly scope = signal<ReportScope>('national');
  readonly selectedRegion = signal<string | null>(null);
  readonly selectedProvince = signal<string | null>(null);
  readonly selectedCity = signal<string | null>(null);

  readonly topReports = signal<FloodReport[]>([]);
  readonly isLoading = signal(false);

  // Broad unfiltered sample used only to populate the region/province/city dropdowns
  private readonly filterSample = signal<FloodReport[]>([]);

  readonly availableRegions = computed(() => this.distinct(this.filterSample().map(r => r.region)));

  readonly availableProvinces = computed(() => {
    const region = this.selectedRegion();
    return this.distinct(
      this.filterSample()
        .filter(r => !region || r.region === region)
        .map(r => r.province)
    );
  });

  readonly availableCities = computed(() => {
    const region = this.selectedRegion();
    const province = this.selectedProvince();
    return this.distinct(
      this.filterSample()
        .filter(r => (!region || r.region === region) && (!province || r.province === province))
        .map(r => r.city)
    );
  });

  init(): void {
    this.api.getTop({ limit: FILTER_SAMPLE_LIMIT }).subscribe(reports => this.filterSample.set(reports));
    this.refresh();
  }

  setScope(scope: ReportScope): void {
    this.scope.set(scope);
    if (scope === 'national') {
      this.selectedRegion.set(null);
      this.selectedProvince.set(null);
      this.selectedCity.set(null);
    }
    this.refresh();
  }

  setRegion(region: string | null): void {
    this.selectedRegion.set(region || null);
    this.selectedProvince.set(null);
    this.selectedCity.set(null);
    this.refresh();
  }

  setProvince(province: string | null): void {
    this.selectedProvince.set(province || null);
    this.selectedCity.set(null);
    this.refresh();
  }

  setCity(city: string | null): void {
    this.selectedCity.set(city || null);
    this.refresh();
  }

  refresh(): void {
    this.isLoading.set(true);
    this.api
      .getTop({
        region: this.selectedRegion() ?? undefined,
        province: this.selectedProvince() ?? undefined,
        city: this.selectedCity() ?? undefined,
        limit: TOP_REPORTS_LIMIT
      })
      .subscribe({
        next: reports => {
          this.topReports.set(reports);
          this.isLoading.set(false);
        },
        error: () => this.isLoading.set(false)
      });
  }

  private distinct(values: (string | undefined)[]): string[] {
    return Array.from(new Set(values.filter((v): v is string => !!v))).sort();
  }
}
