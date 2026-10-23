import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { I18nService, TranslatePipe } from '@masidbaha/shared';

@Component({
  selector: 'app-offline-banner',
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (isOffline() || isShowingCachedData()) {
      <div class="map-page__offline-banner" role="status">
        @if (isOffline()) {
          <span>{{ 'offline.none' | t }}</span>
        }
        @if (isShowingCachedData()) {
          <span>{{
            'offline.cached' | t: { when: cachedDataSavedAt() ? ' (' + i18n.timeAgo(cachedDataSavedAt()!) + ')' : '' }
          }}</span>
        }
      </div>
    }
  `,
  styleUrl: './offline-banner.component.scss'
})
export class OfflineBannerComponent {
  readonly isOffline = input(false);
  readonly isShowingCachedData = input(false);
  readonly cachedDataSavedAt = input<string | null>(null);

  protected readonly i18n = inject(I18nService);
}
