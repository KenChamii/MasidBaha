import {
  AfterViewInit,
  Component,
  ElementRef,
  OnDestroy,
  OnInit,
  effect,
  inject,
  signal,
  untracked,
  viewChild
} from '@angular/core';
import * as L from 'leaflet';
import { Subscription, fromEvent } from 'rxjs';
import { debounceTime } from 'rxjs/operators';

import { FloodReportsStore } from './state/flood-reports.store';
import { buildClusterIcon, buildPopupHtml, createClusterGroup, floodMarker, worstSeverity } from './map-markers';
import { OfflineBannerComponent } from './components/offline-banner/offline-banner.component';
import { PushToggleComponent } from './components/push-toggle/push-toggle.component';
import { ReportFormComponent } from './components/report-form/report-form.component';
import { WatchAreasComponent } from './components/watch-areas/watch-areas.component';
import { WeatherLayersComponent } from './components/weather-layers/weather-layers.component';
import { WeatherLayersStore } from './weather/weather-layers.store';
import { WeatherOverlays } from './weather/weather-overlays';
import { TopReportsPanelComponent } from './components/top-reports-panel/top-reports-panel.component';

import { SignalRService } from '../../core/services/signalr.service';
import { GeolocationService } from '../../core/services/geolocation.service';
import { OfflineCacheService } from '../../core/services/offline-cache.service';
import { WatchAreaService } from '../../core/services/watch-area.service';
import { ActivatedRoute } from '@angular/router';
import {
  FloodReport,
  FloodReportService,
  I18nService,
  LangToggleComponent,
  ModalComponent,
  ThemeToggleComponent,
  ToastService,
  TranslatePipe
} from '@masidbaha/shared';
import { shareReport } from './share-report';

const PHILIPPINES_CENTER: L.LatLngExpression = [12.8797, 121.774];
const DEFAULT_ZOOM = 6;
const LOCATED_ZOOM = 14;
const DEFAULT_RADIUS_METERS = 5000;

@Component({
  selector: 'app-map-page',
  imports: [
    OfflineBannerComponent,
    PushToggleComponent,
    WatchAreasComponent,
    WeatherLayersComponent,
    ReportFormComponent,
    TopReportsPanelComponent,
    ModalComponent,
    LangToggleComponent,
    ThemeToggleComponent,
    TranslatePipe
  ],
  providers: [FloodReportsStore, WeatherLayersStore],
  templateUrl: './map-page.component.html',
  styleUrls: ['./map-page.component.scss']
})
export class MapPageComponent implements AfterViewInit, OnInit, OnDestroy {
  private readonly mapContainer = viewChild.required<ElementRef<HTMLDivElement>>('mapContainer');

  private readonly floodReportService = inject(FloodReportService);
  private readonly signalRService = inject(SignalRService);
  private readonly geolocationService = inject(GeolocationService);
  private readonly offlineCacheService = inject(OfflineCacheService);
  private readonly watchAreaService = inject(WatchAreaService);
  private readonly route = inject(ActivatedRoute);
  private readonly weatherStore = inject(WeatherLayersStore);
  private readonly toast = inject(ToastService);
  private readonly i18n = inject(I18nService);
  readonly store = inject(FloodReportsStore);

  private map!: L.Map;
  private markers = new Map<string, L.Marker>();
  // Reports close together are drawn as one numbered badge until the map is zoomed in
  private cluster!: L.MarkerClusterGroup;
  // The places this visitor watches, drawn as circles so the alert area is visible
  private watchLayer?: L.LayerGroup;
  // Rain radar, satellite rainfall and typhoons, switched from the weather panel
  private weatherOverlays?: WeatherOverlays;
  private subscriptions: Subscription[] = [];
  // Popup content gets rebuilt from partial data on live updates (see
  // reportUpdated$ below), which doesn't carry photoUrl — so we remember it
  // here per report id instead of losing it on every rebuild.
  private reportPhotoUrls = new Map<string, string | undefined>();
  // The place name per report, for the words in a shared message
  private reportPlaces = new Map<string, string | null>();
  // The confidence per report, so a popup can be redrawn in another language
  private reportConfidences = new Map<string, number>();

  // Where the map is centred: a new watch area is added here
  protected readonly mapCenter = signal<{ lat: number; lng: number }>({
    lat: 12.8797,
    lng: 121.774
  });

  protected readonly pinDropMode = signal(false);
  protected readonly pendingPin = signal<{ lat: number; lng: number } | null>(null);
  // the list covers most of a phone screen, so start it collapsed there
  protected readonly showList = signal(window.innerWidth > 640);
  protected readonly viewingPhotoUrl = signal<string | null>(null);

  // offline / PWA fallback (see OfflineCacheService for the why)
  protected readonly isOffline = signal(!navigator.onLine);
  protected readonly isShowingCachedData = signal(false);
  protected readonly cachedDataSavedAt = signal<string | null>(null);

  constructor() {
    // Redraw the watched places whenever the list changes (the map exists once the view is ready)
    effect(() => this.drawWatchAreas(this.watchAreaService.areas()));

    // Words already on the map (marker popups, storm popups) follow the language switch
    effect(() => {
      this.i18n.lang();
      untracked(() => {
        this.markers.forEach((marker, id) => this.refreshPopupContent(marker, id, this.reportConfidences.get(id) ?? 0));
        this.weatherOverlays?.refreshLanguage();
      });
    });

    // Keep the weather layers on the map in step with the panel
    effect(() => {
      // Read the state first: the overlay does not exist until the view is ready, and an effect only
      // watches what it actually reads
      const state = this.weatherStore.state();
      this.weatherOverlays?.sync(state);
    });
  }

  // ---- lifecycle ----

  ngAfterViewInit(): void {
    // The map container <div> must exist in the DOM first, which is why this
    // runs in ngAfterViewInit, not ngOnInit.
    this.map = L.map(this.mapContainer().nativeElement).setView(PHILIPPINES_CENTER, DEFAULT_ZOOM);

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '&copy; OpenStreetMap contributors',
      maxZoom: 19
    }).addTo(this.map);

    if (this.route.snapshot.queryParamMap.has('report')) this.openSharedReport();
    else this.startAtVisitor();

    // Refetch on pan/zoom, debounced so we don't spam the API.
    this.subscriptions.push(
      fromEvent(this.map, 'moveend')
        .pipe(debounceTime(500))
        .subscribe(() => this.reloadAtCenter())
    );

    // Keep the "middle of the map" current for the watch-areas panel
    this.map.on('moveend', () => {
      const { lat, lng } = this.map.getCenter();
      this.mapCenter.set({ lat, lng });
    });

    this.cluster = createClusterGroup({
      maxClusterRadius: 48,
      showCoverageOnHover: false,
      // many reports on one street: fan them out at the deepest zoom instead of stacking them
      spiderfyOnMaxZoom: true,
      disableClusteringAtZoom: 17,
      chunkedLoading: true,
      iconCreateFunction: cluster =>
        buildClusterIcon(cluster.getChildCount(), worstSeverity(cluster.getAllChildMarkers()))
    }).addTo(this.map);

    this.watchLayer = L.layerGroup().addTo(this.map);

    this.weatherOverlays = new WeatherOverlays(this.map, (key, params) => this.i18n.t(key, params));
    this.weatherOverlays.onRadarFrame = frame => this.weatherStore.radarFrame.set(frame);
    this.weatherOverlays.sync(this.weatherStore.state());
    this.drawWatchAreas(this.watchAreaService.areas());

    // Map clicks only matter while pin drop mode is active.
    this.map.on('click', (event: L.LeafletMouseEvent) => this.handleMapClick(event));
  }

  ngOnInit(): void {
    // Toasts sit above the report button, not on top of it
    document.documentElement.style.setProperty('--mb-toast-bottom', '92px');

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
    this.subscriptions.push(this.signalRService.reconnected$.subscribe(() => this.reloadAtCenter()));

    this.store.init();

    // Track connectivity so the offline banner reflects reality even if the
    // connection drops mid-session, not just at initial load.
    this.subscriptions.push(fromEvent(window, 'offline').subscribe(() => this.isOffline.set(true)));
    this.subscriptions.push(
      fromEvent(window, 'online').subscribe(() => {
        this.isOffline.set(false);
        // Connection is back: refetch now instead of waiting for the next pan/zoom
        this.reloadAtCenter();
      })
    );
  }

  ngOnDestroy(): void {
    document.documentElement.style.removeProperty('--mb-toast-bottom');
    this.subscriptions.forEach(sub => sub.unsubscribe());
    this.weatherOverlays?.dispose();
    // Without this, navigating away and back to /map throws
    // "Map container is already initialized".
    this.map?.remove();
  }

  protected zoomToStorm(id: number): void {
    this.weatherOverlays?.zoomTo(id);
  }

  // ---- watch areas ----

  private drawWatchAreas(areas: { name: string; lat: number; lng: number; radiusMeters: number }[]): void {
    if (!this.watchLayer) return;

    this.watchLayer.clearLayers();
    for (const area of areas) {
      L.circle([area.lat, area.lng], {
        radius: area.radiusMeters,
        color: '#0077b6',
        weight: 2,
        dashArray: '6 6',
        fillOpacity: 0.06,
        interactive: false
      })
        .bindTooltip(area.name, { permanent: false })
        .addTo(this.watchLayer);
    }
  }

  // Try to centre on the visitor, then load whatever is nearby
  private startAtVisitor(): void {
    this.geolocationService
      .getCurrentPosition()
      .then(({ lat, lng }) => {
        this.map.setView([lat, lng], LOCATED_ZOOM);
        this.loadNearby(lat, lng);
      })
      .catch(() => {
        // Permission denied or unsupported: fall back to the country-wide view
        const center = this.map.getCenter();
        this.loadNearby(center.lat, center.lng);
      });
  }

  // ---- data loading ----

  private reloadAtCenter(): void {
    const center = this.map.getCenter();
    this.loadNearby(center.lat, center.lng);
  }

  private loadNearby(lat: number, lng: number): void {
    this.floodReportService.getNearby(lat, lng, DEFAULT_RADIUS_METERS).subscribe({
      next: reports => {
        this.isShowingCachedData.set(false);
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

      this.isShowingCachedData.set(true);
      this.cachedDataSavedAt.set(cached.savedAt);
      cached.reports.forEach(report => this.upsertMarker(report));
    });
  }

  // ---- markers and popups ----

  private upsertMarker(report: FloodReport): void {
    this.reportPhotoUrls.set(report.id, report.photoUrl);
    this.reportPlaces.set(report.id, report.city ?? report.province ?? null);
    this.reportConfidences.set(report.id, report.confidenceScore);

    const existing = this.markers.get(report.id);
    if (existing) {
      existing.setLatLng([report.lat, report.lng]);
      this.refreshPopupContent(existing, report.id, report.confidenceScore);
      return;
    }

    const marker = floodMarker([report.lat, report.lng], report.severity, !!report.photoUrl).bindPopup(
      this.popupHtml(report.confidenceScore, !!report.photoUrl)
    );
    this.cluster.addLayer(marker);

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
    this.reportConfidences.set(reportId, confidenceScore);
    marker.setPopupContent(this.popupHtml(confidenceScore, hasPhoto));

    if (marker.isPopupOpen()) {
      const popupEl = marker.getPopup()?.getElement();
      if (popupEl) this.wirePopupButtons(reportId, popupEl);
    }
  }

  // A marker inside a cluster is not on the map yet: zoom (or fan the cluster out) until it is, then open it
  private revealMarker(marker: L.Marker): void {
    this.cluster.zoomToShowLayer(marker, () => marker.openPopup());
  }

  private popupHtml(confidenceScore: number, hasPhoto: boolean): string {
    return buildPopupHtml(confidenceScore, hasPhoto, (key, params) => this.i18n.t(key, params));
  }

  private removeMarker(id: string): void {
    const marker = this.markers.get(id);
    if (marker) {
      this.cluster.removeLayer(marker);
      this.markers.delete(id);
    }
  }

  // Scoped to this popup's own DOM element (not document-wide) so we can
  // never wire buttons belonging to a different marker's popup.
  private wirePopupButtons(reportId: string, popupEl: HTMLElement): void {
    const confirmBtn = popupEl.querySelector<HTMLButtonElement>('[data-action="confirm"]');
    const denyBtn = popupEl.querySelector<HTMLButtonElement>('[data-action="deny"]');
    const photoBtn = popupEl.querySelector<HTMLButtonElement>('[data-action="view-photo"]');
    const shareBtn = popupEl.querySelector<HTMLButtonElement>('[data-action="share"]');

    // Not once: sharing can be done again, and it leaves the popup open
    shareBtn?.addEventListener('click', () => void this.share(reportId));
    confirmBtn?.addEventListener('click', () => this.vote(reportId, 1), { once: true });
    denyBtn?.addEventListener('click', () => this.vote(reportId, -1), { once: true });
    photoBtn?.addEventListener(
      'click',
      () => {
        const url = this.reportPhotoUrls.get(reportId);
        if (url) this.openPhoto(url);
      },
      { once: true }
    );
  }

  private async share(reportId: string): Promise<void> {
    const place = this.reportPlaces.get(reportId);
    const text = place ? this.i18n.t('share.textIn', { place }) : this.i18n.t('share.text');
    const result = await shareReport({ id: reportId, place, text }, window.location.origin);

    if (result === 'copied') this.toast.success(this.i18n.t('map.linkCopied'));
    else if (result === 'failed') this.toast.error(this.i18n.t('map.shareFailed'));
  }

  // A shared link lands here as /map?report=<id>: show that report, even if it is far from where the map starts
  private openSharedReport(): void {
    const id = this.route.snapshot.queryParamMap.get('report');
    if (!id) return;

    this.floodReportService.getById(id).subscribe({
      next: report => {
        if (report.status === 1) {
          this.focusReport(report);
          return;
        }
        // Closed reports are not on the live map, so say what happened and show where it was
        this.map.setView([report.lat, report.lng], LOCATED_ZOOM);
        this.toast.show(this.i18n.t(report.status === 2 ? 'map.reportResolved' : 'map.reportClosed'), 'info');
      },
      error: () => this.toast.show(this.i18n.t('map.reportGone'), 'info')
    });
  }

  private vote(reportId: string, voteType: 1 | -1): void {
    this.floodReportService.vote(reportId, voteType).subscribe(() => this.map.closePopup());
  }

  // ---- panel and toolbar actions ----

  toggleList(): void {
    this.showList.update(shown => !shown);
  }

  focusReport(report: FloodReport): void {
    const targetZoom = Math.max(this.map.getZoom(), LOCATED_ZOOM);
    this.map.setView([report.lat, report.lng], targetZoom);

    const existingMarker = this.markers.get(report.id);
    if (existingMarker) {
      this.revealMarker(existingMarker);
      return;
    }

    // The top-reports list is scope-based, so the report may be far outside
    // whatever the map last fetched: pull in markers around it first, then
    // open the popup once it exists.
    this.floodReportService.getNearby(report.lat, report.lng, DEFAULT_RADIUS_METERS).subscribe(reports => {
      reports.forEach(r => this.upsertMarker(r));
      const marker = this.markers.get(report.id);
      if (marker) this.revealMarker(marker);
    });
  }

  openPhoto(url: string): void {
    this.viewingPhotoUrl.set(url);
  }

  closePhoto(): void {
    this.viewingPhotoUrl.set(null);
  }

  // ---- pin drop / create ----

  togglePinDropMode(): void {
    this.pinDropMode.update(on => !on);
    this.pendingPin.set(null);
  }

  private handleMapClick(event: L.LeafletMouseEvent): void {
    if (!this.pinDropMode()) return;
    this.pendingPin.set({ lat: event.latlng.lat, lng: event.latlng.lng });
  }

  closeReportForm(): void {
    this.pendingPin.set(null);
    this.pinDropMode.set(false);
  }
}
