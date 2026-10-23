import { ChangeDetectionStrategy, Component, computed, inject, output, signal } from '@angular/core';
import { ModalComponent } from '@masidbaha/shared';
import { PAGASA_LINKS } from '../../../../core/services/weather.service';
import { WeatherLayersStore } from '../../weather/weather-layers.store';
import { ALERT_COLOR, CATEGORY_COLOR, WIND_COLOR } from '../../weather/weather-overlays';

// The weather button and its panel: which layers to show on the map
@Component({
  selector: 'app-weather-layers',
  imports: [ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './weather-layers.component.html',
  styleUrl: './weather-layers.component.scss'
})
export class WeatherLayersComponent {
  protected readonly store = inject(WeatherLayersStore);

  // Ask the map to fit a storm in view
  readonly zoomToStorm = output<number>();

  protected readonly open = signal(false);
  protected readonly links = PAGASA_LINKS;
  protected readonly alertColor = ALERT_COLOR;
  protected readonly categoryColor = CATEGORY_COLOR;
  protected readonly windColor = WIND_COLOR;

  // Philippine time: that is the clock people read the radar against
  protected readonly frameTime = computed(() => {
    const frame = this.store.radarFrame();
    return frame
      ? new Intl.DateTimeFormat('en-PH', { timeZone: 'Asia/Manila', hour: 'numeric', minute: '2-digit' }).format(
          new Date(frame.time * 1000)
        )
      : null;
  });

  protected readonly updatedAt = computed(() => {
    const generated = this.store.typhoons()?.generatedAt;
    return generated
      ? new Intl.DateTimeFormat('en-PH', { timeZone: 'Asia/Manila', hour: 'numeric', minute: '2-digit' }).format(
          new Date(generated)
        )
      : null;
  });

  protected openPanel(): void {
    this.open.set(true);
    // Show the real number of storms in the panel, even before the layer is switched on
    if (this.store.typhoonStatus() === 'idle') this.store.loadTyphoons();
  }

  protected zoom(id: number): void {
    this.open.set(false);
    this.zoomToStorm.emit(id);
  }
}
