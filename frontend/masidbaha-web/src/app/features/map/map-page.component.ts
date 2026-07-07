import { AfterViewInit, Component, ElementRef, HostListener, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import * as L from 'leaflet';
import { Observable, Subscription, fromEvent } from 'rxjs';
import { debounceTime } from 'rxjs/operators';

import { FloodReportService } from './services/flood-report.service';
import { SignalRService } from '../../core/services/signalr.service';
import { GeolocationService } from '../../core/services/geolocation.service';
import { SessionService } from '../../core/services/session.service';
import { PhotoService } from '../../core/services/photo.service';
import { FloodReport, ReportScope, Severity } from '../../shared/models/flood-report.model';

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

// Every marker pulses; higher severity pulses faster and wider so the
// visual hierarchy between severities still reads instantly on the map.
const PULSE_BY_SEVERITY: Record<Severity, { duration: number; scale: number }> = {
  1: { duration: 3.2, scale: 1.35 },
  2: { duration: 2.6, scale: 1.45 },
  3: { duration: 2.0, scale: 1.6 },
  4: { duration: 1.4, scale: 1.8 }
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
  // Popup content gets rebuilt from partial data on live updates (see
  // reportUpdated$ below), which doesn't carry photoUrl — so we remember it
  // here per report id instead of losing it on every rebuild.
  private reportPhotoUrls = new Map<string, string | undefined>();

  readonly severityOptions: { value: Severity; label: string }[] =
    (Object.keys(SEVERITY_META).map(Number) as Severity[])
      .map(value => ({ value, label: SEVERITY_META[value].label }));

  pinDropMode = false;
  pendingPin: { lat: number; lng: number } | null = null;
  newReport: { severity: Severity; notes: string } = { severity: 1, notes: '' };
  isSubmitting = false;
  showList = true;

  // photo attachment (step 10) — client-side guard mirrors the backend's own
  // checks, so people get instant feedback instead of waiting on a round trip.
  private static readonly MAX_PHOTO_BYTES = 8 * 1024 * 1024;
  private static readonly ALLOWED_PHOTO_TYPES = ['image/jpeg', 'image/png', 'image/webp'];
  selectedPhotoFile: File | null = null;
  photoPreviewUrl: string | null = null;
  photoError: string | null = null;
  isUploadingPhoto = false;

  // full-screen photo viewer (for photos already attached to a report)
  viewingPhotoUrl: string | null = null;

  // top reports (national/regional/provincial/local)
 
  scope: ReportScope = 'national';
  selectedRegion: string | null = null;
  selectedProvince: string | null = null;
  selectedCity: string | null = null;
  topReports: FloodReport[] = [];
  isLoadingTopReports = false;

  private filterSample: FloodReport[] = [];

  get availableRegions(): string[] {
    return this.distinct(this.filterSample.map(r => r.region));
  }

  get availableProvinces(): string[] {
    return this.distinct(
      this.filterSample
        .filter(r => !this.selectedRegion || r.region === this.selectedRegion)
        .map(r => r.province)
    );
  }

  get availableCities(): string[] {
    return this.distinct(
      this.filterSample
        .filter(r =>
          (!this.selectedRegion || r.region === this.selectedRegion) &&
          (!this.selectedProvince || r.province === this.selectedProvince)
        )
        .map(r => r.city)
    );
  }

  private distinct(values: (string | undefined)[]): string[] {
    return Array.from(new Set(values.filter((v): v is string => !!v))).sort();
  }

  constructor(
    private floodReportService: FloodReportService,
    private signalRService: SignalRService,
    private geolocationService: GeolocationService,
    private sessionService: SessionService,
    private photoService: PhotoService
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
          this.refreshPopupContent(marker, update.floodReportId, update.confidenceScore);
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

    // top reports panel
    // Loads once for the current scope, plus a broad unfiltered sample used
 
    // (~20 rows) so this stays cheap.
    this.loadFilterSample();
    this.refreshTopReports();

    this.subscriptions.push(
      this.signalRService.newReport$.subscribe(() => this.refreshTopReports())
    );
    this.subscriptions.push(
      this.signalRService.reportUpdated$.subscribe(() => this.refreshTopReports())
    );
    this.subscriptions.push(
      this.signalRService.removeReport$.subscribe(() => this.refreshTopReports())
    );
  }

  ngOnDestroy(): void {
    this.subscriptions.forEach(sub => sub.unsubscribe());
    // Step 9: without this, navigating away and back to /map throws
    // "Map container is already initialized".
    this.map?.remove();
  }

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

  private upsertMarker(report: FloodReport): void {
    this.reportPhotoUrls.set(report.id, report.photoUrl);

    const existing = this.markers.get(report.id);
    if (existing) {
      existing.setLatLng([report.lat, report.lng]);
      this.refreshPopupContent(existing, report.id, report.confidenceScore);
      return;
    }

    const marker = L.marker([report.lat, report.lng], { icon: this.buildIcon(report.severity, !!report.photoUrl) })
      .addTo(this.map)
      .bindPopup(this.buildPopupHtml(report.id, report.confidenceScore));

    marker.on('popupopen', (e: L.PopupEvent) => {
      const popupEl = e.popup.getElement();
      if (popupEl) this.wirePopupButtons(report.id, popupEl);
    });

    this.markers.set(report.id, marker);
  }

  // setPopupContent() replaces the popup's inner DOM, which wipes out any
  // click listeners wirePopupButtons() had attached.
  private refreshPopupContent(marker: L.Marker, reportId: string, confidenceScore: number): void {
    marker.setPopupContent(this.buildPopupHtml(reportId, confidenceScore));

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

  private buildIcon(severity: Severity, hasPhoto: boolean): L.DivIcon {
    const meta = SEVERITY_META[severity];
    // Every marker pulses now, graduated by severity — higher severity pulses
    // faster and wider, so the hierarchy still reads at a glance even though
    // all four now animate (previously only severity 4 did).
    const pulse = PULSE_BY_SEVERITY[severity];
    const photoBadge = hasPhoto
      ? '<span class="flood-marker__photo-badge">📷</span>'
      : '';

    return L.divIcon({
      className: 'flood-marker',
      html: `
        <div class="flood-marker__badge" style="background:${meta.color}; --pulse-duration:${pulse.duration}s; --pulse-scale:${pulse.scale};">
          <svg class="flood-marker__icon" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
            <path d="M3 9.5c1.6 1.4 3.2 1.4 4.8 0s3.2-1.4 4.8 0 3.2 1.4 4.8 0 3.2-1.4 4.8 0" stroke="#0A1220" stroke-width="2" stroke-linecap="round"/>
            <path d="M3 15c1.6 1.4 3.2 1.4 4.8 0s3.2-1.4 4.8 0 3.2 1.4 4.8 0 3.2-1.4 4.8 0" stroke="#0A1220" stroke-width="2" stroke-linecap="round" opacity="0.55"/>
          </svg>
          ${photoBadge}
        </div>
      `,
      iconSize: [34, 34],
      iconAnchor: [17, 17],
      popupAnchor: [0, -17]
    });
  }
  private buildPopupHtml(reportId: string, confidenceScore: number): string {
    const photoUrl = this.reportPhotoUrls.get(reportId);
    const photoButton = photoUrl
      ? `<button type="button" class="flood-popup__photo-btn" data-action="view-photo">📷 Tingnan ang larawan</button>`
      : '';

    return `
      <div class="flood-popup">
        <p class="flood-popup__confidence">Confidence: ${confidenceScore}</p>
        ${photoButton}
        <div class="flood-popup__actions">
          <button type="button" data-action="confirm">Kumpirmahin</button>
          <button type="button" data-action="deny">Wala na</button>
        </div>
      </div>
    `;
  }

  // Scoped to this popup's own DOM element (not document-wide) so we can
  // never accidentally wire — or fail to find — buttons belonging to a
  // different marker's popup.
  private wirePopupButtons(reportId: string, popupEl: HTMLElement): void {
    const confirmBtn = popupEl.querySelector<HTMLButtonElement>('[data-action="confirm"]');
    const denyBtn = popupEl.querySelector<HTMLButtonElement>('[data-action="deny"]');
    const photoBtn = popupEl.querySelector<HTMLButtonElement>('[data-action="view-photo"]');

    confirmBtn?.addEventListener('click', () => this.vote(reportId, 1), { once: true });
    denyBtn?.addEventListener('click', () => this.vote(reportId, -1), { once: true });
    photoBtn?.addEventListener('click', () => {
      const url = this.reportPhotoUrls.get(reportId);
      if (url) this.openPhotoModal(url);
    }, { once: true });
  }

  private vote(reportId: string, voteType: 1 | -1): void {
    this.floodReportService.vote(reportId, voteType, this.sessionService.sessionId)
      .subscribe(() => this.map.closePopup());
  }

  // ---- list panel ----

  toggleList(): void {
    this.showList = !this.showList;
  }

  setScope(scope: ReportScope): void {
    this.scope = scope;
    if (scope === 'national') {
      this.selectedRegion = null;
      this.selectedProvince = null;
      this.selectedCity = null;
    }
    this.refreshTopReports();
  }

  onRegionChange(region: string): void {
    this.selectedRegion = region || null;
    this.selectedProvince = null;
    this.selectedCity = null;
    this.refreshTopReports();
  }

  onProvinceChange(province: string): void {
    this.selectedProvince = province || null;
    this.selectedCity = null;
    this.refreshTopReports();
  }

  onCityChange(city: string): void {
    this.selectedCity = city || null;
    this.refreshTopReports();
  }

  private loadFilterSample(): void {
    this.floodReportService.getTop({ limit: 200 })
      .subscribe(reports => this.filterSample = reports);
  }

  private refreshTopReports(): void {
    this.isLoadingTopReports = true;
    this.floodReportService.getTop({
      region: this.selectedRegion ?? undefined,
      province: this.selectedProvince ?? undefined,
      city: this.selectedCity ?? undefined,
      limit: 20
    }).subscribe({
      next: reports => {
        this.topReports = reports;
        this.isLoadingTopReports = false;
      },
      error: () => this.isLoadingTopReports = false
    });
  }

  locationLabel(report: FloodReport): string | null {
    return report.city ?? report.province ?? report.region ?? null;
  }

  focusReport(report: FloodReport): void {
    const targetZoom = Math.max(this.map.getZoom(), LOCATED_ZOOM);
    this.map.setView([report.lat, report.lng], targetZoom);

    const existingMarker = this.markers.get(report.id);
    if (existingMarker) {
      existingMarker.openPopup();
      return;
    }

    // The top-reports list is scope-based (national/regional/etc.), so the
    // report may be far outside whatever the map last fetched — pull in
    // markers around it first, then open the popup once it exists.
    this.floodReportService.getNearby(report.lat, report.lng, DEFAULT_RADIUS_METERS)
      .subscribe(reports => {
        reports.forEach(r => this.upsertMarker(r));
        this.markers.get(report.id)?.openPopup();
      });
  }

  severityLabel(severity: Severity): string {
    return SEVERITY_META[severity].label;
  }

  severityColor(severity: Severity): string {
    return SEVERITY_META[severity].color;
  }

  timeAgo(isoDate: string): string {
    const diffMs = Date.now() - new Date(isoDate).getTime();
    const minutes = Math.floor(diffMs / 60000);
    if (minutes < 1) return 'ngayon lang';
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    return `${Math.floor(hours / 24)}d ago`;
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
    this.clearPhoto();
  }

  // ---- photo attachment ----

  onPhotoSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    this.photoError = null;

    if (!file) {
      this.clearPhoto();
      return;
    }

    if (!MapPageComponent.ALLOWED_PHOTO_TYPES.includes(file.type)) {
      this.photoError = 'Tanging JPEG, PNG, o WebP na larawan lang ang tinatanggap.';
      input.value = '';
      return;
    }

    if (file.size > MapPageComponent.MAX_PHOTO_BYTES) {
      this.photoError = 'Ang photo ay dapat hindi lalagpas sa 8MB.';
      input.value = '';
      return;
    }

    if (this.photoPreviewUrl) {
      URL.revokeObjectURL(this.photoPreviewUrl);
    }

    this.selectedPhotoFile = file;
    this.photoPreviewUrl = URL.createObjectURL(file);
  }

  clearPhoto(): void {
    if (this.photoPreviewUrl) {
      URL.revokeObjectURL(this.photoPreviewUrl);
    }
    this.selectedPhotoFile = null;
    this.photoPreviewUrl = null;
    this.photoError = null;
  }

  // ---- photo viewer modal ----

  openPhotoModal(url: string, event?: Event): void {
    // Stops list-item clicks from also triggering focusReport() underneath.
    event?.stopPropagation();
    this.viewingPhotoUrl = url;
  }

  closePhotoModal(): void {
    this.viewingPhotoUrl = null;
  }

  @HostListener('document:keydown.escape')
  onEscapeKey(): void {
    if (this.viewingPhotoUrl) {
      this.closePhotoModal();
    }
  }

  submitNewReport(): void {
    if (!this.pendingPin || this.isSubmitting) return;

    this.isSubmitting = true;

    // Photo is optional — upload it first (if present) to get a URL, then
    // create the report exactly as before. CreateFloodReportRequest's shape
    // never changes; it just receives a real PhotoUrl now instead of none.
    let uploadStep$: Observable<{ url: string }> | null = null;
    if (this.selectedPhotoFile) {
      this.isUploadingPhoto = true;
      uploadStep$ = this.photoService.upload(this.selectedPhotoFile);
    }

    const createReport = (photoUrl: string | undefined) => {
      this.floodReportService.create({
        lat: this.pendingPin!.lat,
        lng: this.pendingPin!.lng,
        severity: this.newReport.severity,
        notes: this.newReport.notes || undefined,
        photoUrl,
        reporterSessionId: this.sessionService.sessionId
      }).subscribe({
        next: () => {
          this.isSubmitting = false;
          this.pendingPin = null;
          this.pinDropMode = false;
          this.newReport = { severity: 1, notes: '' };
          this.clearPhoto();
          // No manual upsertMarker() call here 
        },
        error: () => {
          this.isSubmitting = false;
        }
      });
    };

    if (uploadStep$) {
      uploadStep$.subscribe({
        next: result => {
          this.isUploadingPhoto = false;
          createReport(result.url);
        },
        error: () => {
          this.isUploadingPhoto = false;
          this.isSubmitting = false;
          this.photoError = 'Nabigo ang pag-upload ng photo. Subukan ulit.';
        }
      });
    } else {
      createReport(undefined);
    }
  }
}
