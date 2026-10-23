import { AfterViewInit, Component, ElementRef, NgZone, OnDestroy, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Severity, timeAgo } from '@masidbaha/shared';
import * as L from 'leaflet';
import { AdminApiService } from '../../core/api/admin-api.service';
import { AdminMapPoint, AdminReportStatus, STATUS_LABEL } from '../../core/api/admin.models';
import { SEVERITY_COLOR, SEVERITY_NAME } from '../../core/util';

// The Philippines, wide enough to show Luzon, the Visayas and Mindanao
const PH_CENTER: L.LatLngTuple = [12.5, 122.5];

@Component({
  selector: 'app-admin-map-page',
  imports: [FormsModule],
  templateUrl: './admin-map-page.component.html',
  styles: `
    :host {
      display: block;
    }
    .map {
      height: calc(100vh - 230px);
      min-height: 360px;
      border: var(--mb-border);
    }
    .legend {
      display: flex;
      flex-wrap: wrap;
      gap: 14px;
      font-size: 0.8rem;
      margin-top: 0.5rem;
    }
  `
})
export class AdminMapPageComponent implements AfterViewInit, OnDestroy {
  private readonly api = inject(AdminApiService);
  private readonly router = inject(Router);
  private readonly zone = inject(NgZone);
  private readonly mapElement = viewChild.required<ElementRef<HTMLDivElement>>('map');

  protected readonly severities: Severity[] = [1, 2, 3, 4];
  protected readonly severityName = SEVERITY_NAME;
  protected readonly severityColor = SEVERITY_COLOR;

  protected readonly status = signal<AdminReportStatus | ''>(1);
  protected readonly severity = signal<Severity | ''>('');
  protected readonly includeHidden = signal(false);
  protected readonly count = signal(0);
  protected readonly isLoading = signal(false);

  private map?: L.Map;
  private layer?: L.LayerGroup;

  ngAfterViewInit(): void {
    // Leaflet manages its own DOM and events, so it stays outside Angular's change detection
    this.zone.runOutsideAngular(() => {
      this.map = L.map(this.mapElement().nativeElement).setView(PH_CENTER, 6);
      L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '© OpenStreetMap contributors',
        maxZoom: 18
      }).addTo(this.map);
      this.layer = L.layerGroup().addTo(this.map);
    });
    this.load();
  }

  ngOnDestroy(): void {
    this.map?.remove();
  }

  protected load(): void {
    this.isLoading.set(true);
    this.api
      .getMapPoints({
        status: this.status() || null,
        severity: this.severity() || null,
        hidden: this.includeHidden() ? null : false
      })
      .subscribe({
        next: points => {
          this.draw(points);
          this.count.set(points.length);
          this.isLoading.set(false);
        },
        error: () => this.isLoading.set(false)
      });
  }

  private draw(points: AdminMapPoint[]): void {
    this.zone.runOutsideAngular(() => {
      this.layer?.clearLayers();

      for (const point of points) {
        const marker = L.circleMarker([point.lat, point.lng], {
          radius: 8,
          color: point.isHidden ? '#888' : '#0b1f33',
          weight: 2,
          dashArray: point.isHidden ? '3 3' : undefined,
          fillColor: SEVERITY_COLOR[point.severity],
          fillOpacity: point.status === 1 ? 0.9 : 0.4
        });

        const content = document.createElement('div');
        const title = document.createElement('strong');
        title.textContent = point.city ?? 'Unknown place';
        const detail = document.createElement('div');
        detail.textContent = `${SEVERITY_NAME[point.severity]} · ${STATUS_LABEL[point.status]} · ${timeAgo(point.reportedAt)}${point.isHidden ? ' · hidden' : ''}`;
        const open = document.createElement('button');
        open.type = 'button';
        open.textContent = 'Open report';
        open.addEventListener('click', () => this.zone.run(() => void this.router.navigate(['/reports', point.id])));
        content.append(title, detail, open);

        marker.bindPopup(content).addTo(this.layer!);
      }
    });
  }
}
