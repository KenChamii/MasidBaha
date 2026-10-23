import { ChangeDetectionStrategy, Component, inject, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { FloodReportsStore } from '../../state/flood-reports.store';
import { FloodReport, ReportScope } from '../../../../shared/models/flood-report.model';
import { severityColor, severityLabel } from '../../../../shared/models/severity';
import { timeAgo } from '../../../../shared/utils/time-ago';

@Component({
    selector: 'app-top-reports-panel',
    imports: [FormsModule, RouterLink],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './top-reports-panel.component.html',
    styleUrl: './top-reports-panel.component.scss'
})
export class TopReportsPanelComponent {
  readonly store = inject(FloodReportsStore);

  readonly reportFocused = output<FloodReport>();
  readonly photoRequested = output<string>();

  readonly scopes: { value: ReportScope; label: string }[] = [
    { value: 'national', label: 'Bansa' },
    { value: 'region', label: 'Rehiyon' },
    { value: 'province', label: 'Probinsya' },
    { value: 'city', label: 'Lungsod' }
  ];

  readonly severityLabel = severityLabel;
  readonly severityColor = severityColor;
  readonly timeAgo = timeAgo;

  locationLabel(report: FloodReport): string | null {
    return report.city ?? report.province ?? report.region ?? null;
  }

  openPhoto(url: string, event: Event): void {
    // Keeps the row's own click (focus on map) from firing underneath
    event.stopPropagation();
    this.photoRequested.emit(url);
  }
}
