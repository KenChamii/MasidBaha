import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ToastService, timeAgo } from '@masidbaha/shared';
import { AdminApiService } from '../../core/api/admin-api.service';
import { AdminAuthService } from '../../core/api/admin-auth.service';
import { BulkAction, FLAG_LABEL, ModerationQueueItem } from '../../core/api/admin.models';
import { SEVERITY_COLOR, SEVERITY_NAME, locationOf, problemMessage } from '../../core/util';
import { ConfirmService } from '../../ui/confirm.service';
import { PagerComponent } from '../../ui/pager.component';
import { SkeletonRowComponent } from '../../ui/skeleton-row.component';

@Component({
  selector: 'app-moderation-page',
  imports: [RouterLink, PagerComponent, SkeletonRowComponent],
  templateUrl: './moderation-page.component.html'
})
export class ModerationPageComponent implements OnInit {
  // placeholder rows while the table loads
  protected readonly skeletonRows = [1, 2, 3, 4, 5];

  private readonly api = inject(AdminApiService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  protected readonly auth = inject(AdminAuthService);

  protected readonly flagLabel = FLAG_LABEL;
  protected readonly severityName = SEVERITY_NAME;
  protected readonly severityColor = SEVERITY_COLOR;
  protected readonly timeAgo = timeAgo;
  protected readonly locationOf = locationOf;

  protected readonly items = signal<ModerationQueueItem[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = 25;
  protected readonly isLoading = signal(false);
  protected readonly selected = signal<ReadonlySet<string>>(new Set());

  protected readonly allSelected = computed(
    () => this.items().length > 0 && this.items().every(item => this.selected().has(item.report.id))
  );

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.isLoading.set(true);
    this.api.getQueue(this.page(), this.pageSize).subscribe({
      next: result => {
        this.items.set(result.items);
        this.totalCount.set(result.totalCount);
        // Only keep ticks for rows that are still in the queue
        const present = new Set(result.items.map(item => item.report.id));
        this.selected.update(set => new Set([...set].filter(id => present.has(id))));
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    });
  }

  protected goToPage(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected toggle(id: string): void {
    this.selected.update(set => {
      const next = new Set(set);
      if (!next.delete(id)) next.add(id);
      return next;
    });
  }

  protected toggleAll(): void {
    this.selected.set(this.allSelected() ? new Set() : new Set(this.items().map(item => item.report.id)));
  }

  protected async apply(action: BulkAction, label: string, danger = false): Promise<void> {
    const ids = [...this.selected()];
    if (ids.length === 0) return;

    const confirmed = await this.confirm.ask({
      title: `${label} ${ids.length} report${ids.length === 1 ? '' : 's'}?`,
      message: danger ? 'This cannot be undone.' : 'You can still change them afterwards.',
      confirmLabel: label,
      danger
    });
    if (!confirmed) return;

    this.api.bulk(ids, action).subscribe({
      next: result => {
        this.toast.success(`${label}: ${result.affectedIds.length} report(s) updated.`);
        this.selected.set(new Set());
        this.load();
      },
      error: err => this.toast.error(problemMessage(err))
    });
  }
}
