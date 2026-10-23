import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '@masidbaha/shared';
import { AdminApiService } from '../../core/api/admin-api.service';
import { AdminAuthService } from '../../core/api/admin-auth.service';
import { Broadcast, BroadcastArea } from '../../core/api/admin.models';
import { problemMessage } from '../../core/util';
import { ConfirmService } from '../../ui/confirm.service';
import { PagerComponent } from '../../ui/pager.component';

// Quick picks for the "only people near…" option; "custom" lets staff type coordinates
export const AREA_PRESETS = [
  { id: 'manila', label: 'Metro Manila', lat: 14.5995, lng: 120.9842 },
  { id: 'cebu', label: 'Cebu City', lat: 10.3157, lng: 123.8854 },
  { id: 'davao', label: 'Davao City', lat: 7.1907, lng: 125.4553 },
  { id: 'iloilo', label: 'Iloilo City', lat: 10.7202, lng: 122.5621 },
  { id: 'cdo', label: 'Cagayan de Oro', lat: 8.4542, lng: 124.6319 },
  { id: 'baguio', label: 'Baguio', lat: 16.4023, lng: 120.596 },
  { id: 'tacloban', label: 'Tacloban', lat: 11.2543, lng: 125.0 }
];

@Component({
  selector: 'app-broadcast-page',
  imports: [FormsModule, DatePipe, PagerComponent],
  templateUrl: './broadcast-page.component.html'
})
export class BroadcastPageComponent implements OnInit {
  private readonly api = inject(AdminApiService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  protected readonly auth = inject(AdminAuthService);

  protected readonly title = signal('');
  protected readonly body = signal('');
  protected readonly url = signal('/map');

  // Optional audience: only people who watch a place inside this circle
  protected readonly limitToArea = signal(false);
  protected readonly areaChoice = signal('manila');
  protected readonly customLat = signal<number | null>(null);
  protected readonly customLng = signal<number | null>(null);
  protected readonly areaRadiusKm = signal(30);
  protected readonly presets = AREA_PRESETS;
  protected readonly error = signal<string | null>(null);
  protected readonly sending = signal(false);

  protected readonly history = signal<Broadcast[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = 10;

  ngOnInit(): void {
    this.load();
  }

  protected goToPage(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected load(): void {
    this.api.getBroadcasts(this.page(), this.pageSize).subscribe(result => {
      this.history.set(result.items);
      this.totalCount.set(result.totalCount);
    });
  }

  // The circle the announcement is limited to, or null for everybody
  private area(): BroadcastArea | null {
    if (!this.limitToArea()) return null;

    const radiusMeters = Math.round(this.areaRadiusKm() * 1000);
    if (this.areaChoice() === 'custom') {
      const lat = this.customLat();
      const lng = this.customLng();
      return lat === null || lng === null ? null : { lat, lng, radiusMeters };
    }

    const preset = AREA_PRESETS.find(p => p.id === this.areaChoice());
    return preset ? { lat: preset.lat, lng: preset.lng, radiusMeters } : null;
  }

  protected async send(): Promise<void> {
    if (this.sending()) return;

    const area = this.area();
    if (this.limitToArea() && !area) {
      this.error.set('Enter the latitude and longitude of the area.');
      return;
    }

    const confirmed = await this.confirm.ask({
      title: area ? 'Send to people near that area?' : 'Send to every subscriber?',
      message: area
        ? 'Only people who chose to watch a place inside that circle get this notification. It cannot be recalled.'
        : 'A push notification goes to everyone who turned alerts on. It cannot be recalled.',
      confirmLabel: 'Send now'
    });
    if (!confirmed) return;

    this.sending.set(true);
    this.error.set(null);

    this.api.sendBroadcast(this.title().trim(), this.body().trim(), this.url().trim(), area).subscribe({
      next: sent => {
        this.sending.set(false);
        this.toast.success(`Sent to ${sent.recipients} subscriber(s).`);
        this.title.set('');
        this.body.set('');
        this.page.set(1);
        this.load();
      },
      error: err => {
        this.sending.set(false);
        this.error.set(problemMessage(err, 'Could not send the announcement.'));
      }
    });
  }
}
