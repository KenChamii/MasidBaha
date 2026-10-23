import { ChangeDetectionStrategy, Component, inject, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { FloodReportsStore } from '../../state/flood-reports.store';
import { FloodReport, I18nService, ReportScope, TranslatePipe, severityColor } from '@masidbaha/shared';

@Component({
  selector: 'app-top-reports-panel',
  imports: [FormsModule, RouterLink, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './top-reports-panel.component.html',
  styleUrl: './top-reports-panel.component.scss'
})
export class TopReportsPanelComponent {
  readonly store = inject(FloodReportsStore);
  protected readonly i18n = inject(I18nService);

  readonly reportFocused = output<FloodReport>();
  readonly photoRequested = output<string>();

  readonly scopes: { value: ReportScope; label: string }[] = [
    { value: 'national', label: 'scope.national' },
    { value: 'region', label: 'scope.region' },
    { value: 'province', label: 'scope.province' },
    { value: 'city', label: 'scope.city' }
  ];

  readonly severityColor = severityColor;

  // placeholders shown while the list loads
  protected readonly skeletonRows = [1, 2, 3, 4];

  locationLabel(report: FloodReport): string | null {
    return report.city ?? report.province ?? report.region ?? null;
  }

  openPhoto(url: string, event: Event): void {
    // Keeps the row's own click (focus on map) from firing underneath
    event.stopPropagation();
    this.photoRequested.emit(url);
  }
}
