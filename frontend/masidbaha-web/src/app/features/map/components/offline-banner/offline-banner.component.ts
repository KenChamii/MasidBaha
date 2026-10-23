import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { timeAgo } from '../../../../shared/utils/time-ago';

@Component({
  selector: 'app-offline-banner',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (isOffline() || isShowingCachedData()) {
      <div class="map-page__offline-banner" role="status">
        @if (isOffline()) {
          <span>📶 Walang internet connection.</span>
        }
        @if (isShowingCachedData()) {
          <span> Ipinapakita ang huling na-fetch na data{{ cachedDataSavedAt() ? ' (' + timeAgo(cachedDataSavedAt()!) + ')' : '' }}.</span>
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

  readonly timeAgo = timeAgo;
}
