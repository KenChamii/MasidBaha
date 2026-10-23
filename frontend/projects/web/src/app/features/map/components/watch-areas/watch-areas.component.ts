import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { I18nService, ModalComponent, TranslatePipe } from '@masidbaha/shared';
import { PushNotificationService } from '../../../../core/services/push-notification.service';
import { MAX_WATCH_AREAS, WatchAreaService } from '../../../../core/services/watch-area.service';

export const RADIUS_CHOICES = [1000, 2000, 5000, 10000, 20000];

// "Watch this place": the button that opens a panel to choose where alerts should come from
@Component({
  selector: 'app-watch-areas',
  imports: [FormsModule, ModalComponent, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './watch-areas.component.html',
  styleUrl: './watch-areas.component.scss'
})
export class WatchAreasComponent {
  private readonly push = inject(PushNotificationService);
  protected readonly watchAreas = inject(WatchAreaService);
  private readonly i18n = inject(I18nService);

  // The middle of the map, which is where a new place is added
  readonly center = input.required<{ lat: number; lng: number }>();

  // Areas only exist next to a push subscription, so the button appears once alerts are on
  private readonly subscribed = toSignal(this.push.subscription$, { initialValue: null });
  protected readonly visible = computed(() => this.push.isSupported && !!this.subscribed());

  protected readonly open = signal(false);
  protected readonly name = signal('');
  protected readonly radius = signal(2000);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly radiusChoices = RADIUS_CHOICES;
  protected readonly maxAreas = MAX_WATCH_AREAS;
  protected readonly isFull = computed(() => this.watchAreas.areas().length >= MAX_WATCH_AREAS);

  constructor() {
    // Fetch the saved places once alerts are known to be on (and forget them when they go off)
    effect(() => {
      if (this.visible()) void this.watchAreas.load().catch(() => undefined);
      else this.watchAreas.clear();
    });
  }

  protected kilometres(meters: number): string {
    return `${meters / 1000} km`;
  }

  protected async add(): Promise<void> {
    if (this.busy() || this.isFull()) return;

    const { lat, lng } = this.center();
    const name = this.name().trim() || this.i18n.t('watch.defaultName', { n: this.watchAreas.areas().length + 1 });

    this.busy.set(true);
    this.error.set(null);
    try {
      await this.watchAreas.add(name, lat, lng, this.radius());
      this.name.set('');
    } catch (err) {
      this.error.set(this.messageFor(err));
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(id: string): Promise<void> {
    this.error.set(null);
    try {
      await this.watchAreas.remove(id);
    } catch (err) {
      this.error.set(this.messageFor(err));
    }
  }

  // The API's problem details carry a plain-language reason (outside the Philippines, too many places...)
  private messageFor(err: unknown): string {
    if (err instanceof HttpErrorResponse) {
      const validation = err.error?.errors as Record<string, string[]> | undefined;
      return Object.values(validation ?? {}).flat()[0] ?? err.error?.detail ?? this.i18n.t('watch.error');
    }
    return this.i18n.t('watch.error');
  }
}
