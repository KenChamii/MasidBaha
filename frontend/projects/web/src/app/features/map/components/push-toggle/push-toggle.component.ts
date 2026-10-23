import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { I18nService, TranslatePipe } from '@masidbaha/shared';
import { PushNotificationService } from '../../../../core/services/push-notification.service';

@Component({
  selector: 'app-push-toggle',
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (supported) {
      <button
        type="button"
        class="map-page__push-toggle"
        [class.map-page__push-toggle--active]="enabled()"
        [disabled]="isToggling()"
        (click)="toggle()"
        [title]="(enabled() ? 'push.turnOff' : 'push.turnOn') | t"
        [attr.aria-pressed]="enabled()"
      >
        {{ enabled() ? '🔔' : '🔕' }}
      </button>

      @if (error()) {
        <div class="map-page__push-error" role="alert">
          {{ error() }}
          <button type="button" (click)="error.set(null)" [attr.aria-label]="'common.dismiss' | t">✕</button>
        </div>
      }
    }
  `,
  styleUrl: './push-toggle.component.scss'
})
export class PushToggleComponent {
  private readonly push = inject(PushNotificationService);
  private readonly i18n = inject(I18nService);

  // Needs a browser and build that support push (production build, HTTPS or localhost)
  readonly supported = this.push.isSupported;
  readonly enabled = signal(false);
  readonly isToggling = signal(false);
  readonly error = signal<string | null>(null);

  constructor() {
    if (this.supported) {
      // Reflect any subscription the browser already has from a previous visit
      this.push.subscription$
        .pipe(takeUntilDestroyed(inject(DestroyRef)))
        .subscribe((sub: PushSubscription | null) => this.enabled.set(!!sub));
    }
  }

  async toggle(): Promise<void> {
    if (this.isToggling()) return;

    this.isToggling.set(true);
    this.error.set(null);

    try {
      if (this.enabled()) {
        await this.push.unsubscribe();
      } else {
        await this.push.subscribe();
      }
      // enabled updates through the subscription$ listener above
    } catch (err) {
      // Usually means the person blocked the browser's permission prompt
      this.error.set(err instanceof Error ? err.message : this.i18n.t('push.failed'));
    } finally {
      this.isToggling.set(false);
    }
  }
}
