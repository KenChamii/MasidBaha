import { AfterViewInit, Component, ElementRef, OnDestroy, OnInit, ViewChild, inject, ChangeDetectionStrategy } from '@angular/core';
import * as L from 'leaflet';
import { Subscription, fromEvent } from 'rxjs';
import { debounceTime } from 'rxjs/operators';

import { FloodReportService } from './services/flood-report.service';
import { FloodReportsStore } from './state/flood-reports.store';
import { buildFloodIcon, buildPopupHtml } from './map-markers';
import { OfflineBannerComponent } from './components/offline-banner/offline-banner.component';
import { PushToggleComponent } from './components/push-toggle/push-toggle.component';
import { ReportFormComponent } from './components/report-form/report-form.component';
import { TopReportsPanelComponent } from './components/top-reports-panel/top-reports-panel.component';
import { ModalComponent } from '../../shared/ui/modal/modal.component';
import { ThemeToggleComponent } from '../../shared/ui/theme-toggle/theme-toggle.component';
import { SignalRService } from '../../core/services/signalr.service';
import { GeolocationService } from '../../core/services/geolocation.service';
import { OfflineCacheService } from '../../core/services/offline-cache.service';
import { FloodReport } from '../../shared/models/flood-report.model';

const PHILIPPINES_CENTER: L.LatLngExpression = [12.8797, 121.7740];
const DEFAULT_ZOOM = 6;
const LOCATED_ZOOM = 14;
const DEFAULT_RADIUS_METERS = 5000;

@Component({
    selector: 'app-map-page',
    imports: [
        OfflineBannerComponent,
        PushToggleComponent,
        ReportFormComponent,
        TopReportsPanelComponent,
        ModalComponent,
        ThemeToggleComponent
    ],
    providers: [FloodReportsStore],
    templateUrl: './map-page.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    styleUrls: ['./map-page.component.scss']
})
export class MapPageComponent implements AfterViewInit, OnInit, OnDestroy {
  @ViewChild('mapContainer', { static: true }) mapContainerRef!: ElementRef<HTMLDivElement>;

  private readonly floodReportService = inject(FloodReportService);
  private readonly signalRService = inject(SignalRService);
  private readonly geolocationService = inject(GeolocationService);
  private readonly offlineCacheService = inject(OfflineCacheService);
  readonly store = inject(FloodReportsStore);

  private map!: L.Map;
  private markers = new Map<string, L.Marker>();
  private subscriptions: Subscription[] = [];
  // Popup content gets rebuilt from partial data on live updates (see
  // reportUpdated$ below), which doesn't carry photoUrl — so we remember it
  // here per report id instead of losing it on every rebuild.
  private reportPhotoUrls = new Map<string, string | undefined>();

  pinDropMode = false;
  pendingPin: { lat: number; lng: number } | null = null;
  // the list covers most of a phone screen, so start it collapsed there
  showList = window.innerWidth > 640;
  viewingPhotoUrl: string | null = null;

  // offline / PWA fallback (see OfflineCacheService for the why)
  isOffline = !navigator.onLine;
  isShowingCachedData = false;
  cachedDataSavedAt: string | null = null;

  // ---- lifecycle ----

  ngAfterViewInit(): void {
    // The map container <div> must exist in the DOM first, which is why this
    // runs in ngAfterViewInit, not ngOnInit.
    this.map = L.map(this.mapContainerRef.nativeElement).setView(PHILIPPINES_CENTER, DEFAULT_ZOOM);

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '&copy; OpenStreetMap contributors',
      maxZoom: 19
    }).addTo(this.map);

    // Try to center on the user, then load whatever is nearby.
    this.geolocationService.getCurrentPosition()
      .then(({ lat, lng }) => {
        this.map.setView([lat, lng], LOCATED_ZOOM);
        this.loadNearby(lat, lng);
      })
      .catch(() => {
        // Permission denied or unsupported: fall back to the country-wide view
        const center = this.map.getCenter();
        this.loadNearby(center.lat, center.lng);
      });

    // Refetch on pan/zoom, debounced so we don't spam the API.
    this.subscriptions.push(
      fromEvent(this.map, 'moveend').pipe(debounceTime(500)).subscribe(() => this.reloadAtCenter())
    );

    // Map clicks only matter while pin drop mode is active.
    this.map.on('click', (event: L.LeafletMouseEvent) => this.handleMapClick(event));
  }

  ngOnInit(): void {
    // Connect before subscribing, so the socket is already opening while we attach listeners.
    this.signalRService.connect();

    // Live add / update / remove without refetching.
    this.subscriptions.push(
      this.signalRService.newReport$.subscribe(report => {
        this.upsertMarker(report);
        this.store.refresh();
      })
    );

    this.subscriptions.push(
      this.signalRService.reportUpdated$.subscribe(update => {
        // ReportUpdated only carries confidence/status, not lat/lng. If we
        // don't already have this marker (e.g. outside our last fetch
        // radius), there's nothing on the map to refresh.
        const marker = this.markers.get(update.floodReportId);
        if (marker) {
          this.refreshPopupContent(marker, update.floodReportId, update.confidenceScore);
        }
        this.store.refresh();
      })
    );

    this.subscriptions.push(
      this.signalRService.removeReport$.subscribe(id => {
        this.removeMarker(id);
        this.store.refresh();
      })
    );

    // Catch up on anything missed while the socket was down.
    this.subscriptions.push(
      this.signalRService.reconnected$.subscribe(() => this.reloadAtCenter())
    );

    this.store.init();

    // Track connectivity so the offline banner reflects reality even if the
    // connection drops mid-session, not just at initial load.
    this.subscriptions.push(
      fromEvent(window, 'offline').subscribe(() => this.isOffline = true)
    );
    this.subscriptions.push(
      fromEvent(window, 'online').subscribe(() => {
        this.isOffline = false;
        // Connection is back: refetch now instead of waiting for the next pan/zoom
        this.reloadAtCenter();
      })
    );
  }

  ngOnDestroy(): void {
    this.subscriptions.forEach(sub => sub.unsubscribe());
    // Without this, navigating away and back to /map throws
    // "Map container is already initialized".
    this.map?.remove();
  }

  // ---- data loading ----

  private reloadAtCenter(): void {
    const center = this.map.getCenter();
    this.loadNearby(center.lat, center.lng);
  }

  private loadNearby(lat: number, lng: number): void {
    this.floodReportService.getNearby(lat, lng, DEFAULT_RADIUS_METERS)
      .subscribe({
        next: reports => {
          this.isShowingCachedData = false;
          const currentIds = new Set(reports.map(r => r.id));

          // drop markers that fell outside the new radius
          for (const id of this.markers.keys()) {
            if (!currentIds.has(id)) this.removeMarker(id);
          }

          reports.forEach(report => this.upsertMarker(report));

          // Snapshot for offline fallback: fire-and-forget, never blocks the UI on IndexedDB writes.
          this.offlineCacheService.saveLastKnownReports(reports);
        },
        error: () => this.loadFromOfflineCache()
      });
  }

  // Called when a live fetch fails (most likely no network). Shows whatever
  // was last fetched, clearly labeled as cached so it isn't mistaken for a live view.
  private loadFromOfflineCache(): void {
    this.offlineCacheService.getLastKnownReports().then(cached => {
      if (!cached) return;

      this.isShowingCachedData = true;
      this.cachedDataSavedAt = cached.savedAt;
      cached.reports.forEach(report => this.upsertMarker(report));
    });
  }

  // ---- markers and popups ----

  private upsertMarker(report: FloodReport): void {
    this.reportPhotoUrls.set(report.id, report.photoUrl);

    const existing = this.markers.get(report.id);
    if (existing) {
      existing.setLatLng([report.lat, report.lng]);
      this.refreshPopupContent(existing, report.id, report.confidenceScore);
      return;
    }

    const marker = L.marker([report.lat, report.lng], { icon: buildFloodIcon(report.severity, !!report.photoUrl) })
      .addTo(this.map)
      .bindPopup(buildPopupHtml(report.confidenceScore, !!report.photoUrl));

    marker.on('popupopen', (e: L.PopupEvent) => {
      const popupEl = e.popup.getElement();
      if (popupEl) this.wirePopupButtons(report.id, popupEl);
    });

    this.markers.set(report.id, marker);
  }

  // setPopupContent() replaces the popup's inner DOM, which wipes out any
  // click listeners wirePopupButtons() had attached.
  private refreshPopupContent(marker: L.Marker, reportId: string, confidenceScore: number): void {
    const hasPhoto = !!this.reportPhotoUrls.get(reportId);
    marker.setPopupContent(buildPopupHtml(confidenceScore, hasPhoto));

    if (marker.isPopupOpen()) {
      const popupEl = marker.getPopup()?.getElement();
      if (popupEl) this.wirePopupButtons(reportId, popupEl);
    }
  }

  private removeMarker(id: string): void {
    const marker = this.markers.get(id);
    if (marker) {
      this.map.removeLayer(marker);
      this.markers.delete(id);
    }
  }

  // Scoped to this popup's own DOM element (not document-wide) so we can
  // never wire buttons belonging to a different marker's popup.
  private wirePopupButtons(reportId: string, popupEl: HTMLElement): void {
    const confirmBtn = popupEl.querySelector<HTMLButtonElement>('[data-action="confirm"]');
    const denyBtn = popupEl.querySelector<HTMLButtonElement>('[data-action="deny"]');
    const photoBtn = popupEl.querySelector<HTMLButtonElement>('[data-action="view-photo"]');

    confirmBtn?.addEventListener('click', () => this.vote(reportId, 1), { once: true });
    denyBtn?.addEventListener('click', () => this.vote(reportId, -1), { once: true });
    photoBtn?.addEventListener('click', () => {
      const url = this.reportPhotoUrls.get(reportId);
      if (url) this.openPhoto(url);
    }, { once: true });
  }

  private vote(reportId: string, voteType: 1 | -1): void {
    this.floodReportService.vote(reportId, voteType)
      .subscribe(() => this.map.closePopup());
  }

  // ---- panel and toolbar actions ----

  toggleList(): void {
    this.showList = !this.showList;
  }

  focusReport(report: FloodReport): void {
    const targetZoom = Math.max(this.map.getZoom(), LOCATED_ZOOM);
    this.map.setView([report.lat, report.lng], targetZoom);

    const existingMarker = this.markers.get(report.id);
    if (existingMarker) {
      existingMarker.openPopup();
      return;
    }

    // The top-reports list is scope-based, so the report may be far outside
    // whatever the map last fetched: pull in markers around it first, then
    // open the popup once it exists.
    this.floodReportService.getNearby(report.lat, report.lng, DEFAULT_RADIUS_METERS)
      .subscribe(reports => {
        reports.forEach(r => this.upsertMarker(r));
        this.markers.get(report.id)?.openPopup();
      });
  }

  openPhoto(url: string): void {
    this.viewingPhotoUrl = url;
  }

  closePhoto(): void {
    this.viewingPhotoUrl = null;
  }

  // ---- pin drop / create ----

  togglePinDropMode(): void {
    this.pinDropMode = !this.pinDropMode;
    this.pendingPin = null;
  }

  private handleMapClick(event: L.LeafletMouseEvent): void {
    if (!this.pinDropMode) return;
    this.pendingPin = { lat: event.latlng.lat, lng: event.latlng.lng };
  }

  closeReportForm(): void {
    this.pendingPin = null;
    this.pinDropMode = false;
  }
}
