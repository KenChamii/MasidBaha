import { AfterViewInit, Component, ElementRef, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import * as L from 'leaflet';
import { Subscription, fromEvent } from 'rxjs';
import { debounceTime } from 'rxjs/operators';

import { FloodReportService } from './services/flood-report.service';
import { SignalRService } from '../../core/services/signalr.service';
import { GeolocationService } from '../../core/services/geolocation.service';
import { SessionService } from '../../core/services/session.service';
import { FloodReport, Severity } from '../../shared/models/flood-report.model';

const PHILIPPINES_CENTER: L.LatLngExpression = [12.8797, 121.7740];
const DEFAULT_ZOOM = 6;
const LOCATED_ZOOM = 14;
const DEFAULT_RADIUS_METERS = 5000;

const SEVERITY_META: Record<Severity, { label: string; color: string }> = {
  1: { label: 'Madadaanan', color: '#34C6E8' },
  2: { label: 'Tuhod ang lalim', color: '#FFD166' },
  3: { label: 'Baywang ang lalim', color: '#FF7A47' },
  4: { label: 'Hindi madadaanan', color: '#E63946' }
};

@Component({
  selector: 'app-map-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './map-page.component.html',
  styleUrls: ['./map-page.component.scss']
})
export class MapPageComponent implements AfterViewInit, OnInit, OnDestroy {
  @ViewChild('mapContainer', { static: true }) mapContainerRef!: ElementRef<HTMLDivElement>;

  private map!: L.Map;
  private markers = new Map<string, L.Marker>();
  private subscriptions: Subscription[] = [];

  readonly severityOptions: { value: Severity; label: string }[] =
    (Object.keys(SEVERITY_META).map(Number) as Severity[])
      .map(value => ({ value, label: SEVERITY_META[value].label }));

  pinDropMode = false;
  pendingPin: { lat: number; lng: number } | null = null;
  newReport: { severity: Severity; notes: string } = { severity: 1, notes: '' };
  isSubmitting = false;

  constructor(
    private floodReportService: FloodReportService,
    private signalRService: SignalRService,
    private geolocationService: GeolocationService,
    private sessionService: SessionService
  ) {}

  // ---- lifecycle ----

  ngAfterViewInit(): void {
    // Step 1: the map container <div> must exist in the DOM first —
    // that's why this runs in ngAfterViewInit, not ngOnInit.
    this.map = L.map(this.mapContainerRef.nativeElement).setView(PHILIPPINES_CENTER, DEFAULT_ZOOM);

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '&copy; OpenStreetMap contributors',
      maxZoom: 19
    }).addTo(this.map);

    // Step 2: try to center on the user, then load whatever is nearby.
    this.geolocationService.getCurrentPosition()
      .then(({ lat, lng }) => {
        this.map.setView([lat, lng], LOCATED_ZOOM);
        this.loadNearby(lat, lng);
      })
      .catch(() => {
        // Permission denied or unsupported — fall back to the country-wide
        // view, still load whatever is near the default center.
        const center = this.map.getCenter();
        this.loadNearby(center.lat, center.lng);
      });

    // Step 3: refetch on pan/zoom, debounced so we don't spam the API.
    const moveEnd$ = fromEvent(this.map, 'moveend').pipe(debounceTime(500));
    this.subscriptions.push(
      moveEnd$.subscribe(() => {
        const center = this.map.getCenter();
        this.loadNearby(center.lat, center.lng);
      })
    );

    // Map clicks only matter while "pin drop" mode is active (step 8).
    this.map.on('click', (event: L.LeafletMouseEvent) => this.handleMapClick(event));
  }

  ngOnInit(): void {
    // Step 5: connect before subscribing, so the socket is already opening
    // while we attach listeners.
    this.signalRService.connect();

    // Step 6: live add / update / remove without refetching.
    this.subscriptions.push(
      this.signalRService.newReport$.subscribe(report => this.upsertMarker(report))
    );

    this.subscriptions.push(
      this.signalRService.reportUpdated$.subscribe(update => {
        // ReportUpdated only carries confidence/status, not lat/lng — if we
        // don't already have this marker (e.g. outside our last fetch
        // radius), there's nothing on the map to refresh, so skip it.
        const marker = this.markers.get(update.floodReportId);
        if (marker) {
          marker.setPopupContent(this.buildPopupHtml(update.floodReportId, update.confidenceScore));
        }
      })
    );

    this.subscriptions.push(
      this.signalRService.removeReport$.subscribe(id => this.removeMarker(id))
    );

    // Step 7: catch up on anything missed while the socket was down.
    this.subscriptions.push(
      this.signalRService.reconnected$.subscribe(() => {
        const center = this.map.getCenter();
        this.loadNearby(center.lat, center.lng);
      })
    );
  }

  ngOnDestroy(): void {
    this.subscriptions.forEach(sub => sub.unsubscribe());
    // Step 9: without this, navigating away and back to /map throws
    // "Map container is already initialized".
    this.map?.remove();
  }

  // ---- data loading ----

  private loadNearby(lat: number, lng: number): void {
    this.floodReportService.getNearby(lat, lng, DEFAULT_RADIUS_METERS)
      .subscribe(reports => {
        const currentIds = new Set(reports.map(r => r.id));

        // drop markers that fell outside the new radius
        for (const id of this.markers.keys()) {
          if (!currentIds.has(id)) this.removeMarker(id);
        }

        reports.forEach(report => this.upsertMarker(report));
      });
  }

  // ---- markers (step 4) ----

  private upsertMarker(report: FloodReport): void {
    const existing = this.markers.get(report.id);
    if (existing) {
      existing.setLatLng([report.lat, report.lng]);
      existing.setPopupContent(this.buildPopupHtml(report.id, report.confidenceScore));
      return;
    }

    const marker = L.marker([report.lat, report.lng], { icon: this.buildIcon(report.severity) })
      .addTo(this.map)
      .bindPopup(this.buildPopupHtml(report.id, report.confidenceScore));

    marker.on('popupopen', () => this.wirePopupButtons(report.id));

    this.markers.set(report.id, marker);
  }

  private removeMarker(id: string): void {
    const marker = this.markers.get(id);
    if (marker) {
      this.map.removeLayer(marker);
      this.markers.delete(id);
    }
  }

  private buildIcon(severity: Severity): L.DivIcon {
    const color = SEVERITY_META[severity].color;
    return L.divIcon({
      className: 'flood-marker',
      html: `<span class="flood-marker__dot" style="background:${color}"></span>`,
      iconSize: [18, 18],
      iconAnchor: [9, 9]
    });
  }

  // Leaflet popups are raw HTML, not Angular templates — buttons inside
  // them can't use (click) bindings, so we build a string here and wire
  // real DOM listeners in wirePopupButtons() once the popup is in the DOM.
  private buildPopupHtml(reportId: string, confidenceScore: number): string {
    return `
      <div class="flood-popup">
        <p class="flood-popup__confidence">Confidence: ${confidenceScore}</p>
        <div class="flood-popup__actions">
          <button type="button" data-action="confirm" data-id="${reportId}">Kumpirmahin</button>
          <button type="button" data-action="deny" data-id="${reportId}">Wala na</button>
        </div>
      </div>
    `;
  }

  private wirePopupButtons(reportId: string): void {
    const confirmBtn = document.querySelector<HTMLButtonElement>(`[data-action="confirm"][data-id="${reportId}"]`);
    const denyBtn = document.querySelector<HTMLButtonElement>(`[data-action="deny"][data-id="${reportId}"]`);

    confirmBtn?.addEventListener('click', () => this.vote(reportId, 1), { once: true });
    denyBtn?.addEventListener('click', () => this.vote(reportId, -1), { once: true });
  }

  private vote(reportId: string, voteType: 1 | -1): void {
    this.floodReportService.vote(reportId, voteType, this.sessionService.sessionId)
      .subscribe(() => this.map.closePopup());
  }

  // ---- pin drop / create (step 8) ----

  togglePinDropMode(): void {
    this.pinDropMode = !this.pinDropMode;
    this.pendingPin = null;
  }

  private handleMapClick(event: L.LeafletMouseEvent): void {
    if (!this.pinDropMode) return;
    this.pendingPin = { lat: event.latlng.lat, lng: event.latlng.lng };
  }

  cancelPendingPin(): void {
    this.pendingPin = null;
    this.pinDropMode = false;
  }

  submitNewReport(): void {
    if (!this.pendingPin || this.isSubmitting) return;

    this.isSubmitting = true;
    this.floodReportService.create({
      lat: this.pendingPin.lat,
      lng: this.pendingPin.lng,
      severity: this.newReport.severity,
      notes: this.newReport.notes || undefined,
      reporterSessionId: this.sessionService.sessionId
    }).subscribe({
      next: () => {
        this.isSubmitting = false;
        this.pendingPin = null;
        this.pinDropMode = false;
        this.newReport = { severity: 1, notes: '' };
        // No manual upsertMarker() call here — the backend broadcasts
        // "NewReport" over SignalR (Commit 5), and newReport$ (above) picks
        // it up, so the pin the user just dropped appears the same way
        // everyone else's does.
      },
      error: () => {
        this.isSubmitting = false;
      }
    });
  }
}