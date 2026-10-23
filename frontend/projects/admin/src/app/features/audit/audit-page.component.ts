import { DatePipe, JsonPipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AdminApiService } from '../../core/api/admin-api.service';
import { AuditEntry } from '../../core/api/admin.models';
import { PagerComponent } from '../../ui/pager.component';
import { SkeletonRowComponent } from '../../ui/skeleton-row.component';

// Action groups offered in the filter (the API matches by prefix)
const ACTION_GROUPS = [
  { value: '', label: 'Everything' },
  { value: 'auth.', label: 'Sign-ins and passwords' },
  { value: 'report.', label: 'Report changes' },
  { value: 'user.', label: 'Staff accounts' },
  { value: 'ban.', label: 'Bans' },
  { value: 'broadcast.', label: 'Broadcasts' },
  { value: 'settings.', label: 'Settings' }
];

@Component({
  selector: 'app-audit-page',
  imports: [FormsModule, DatePipe, JsonPipe, RouterLink, PagerComponent, SkeletonRowComponent],
  templateUrl: './audit-page.component.html'
})
export class AuditPageComponent implements OnInit {
  // placeholder rows while the table loads
  protected readonly skeletonRows = [1, 2, 3, 4, 5];

  private readonly api = inject(AdminApiService);

  protected readonly groups = ACTION_GROUPS;

  protected readonly entries = signal<AuditEntry[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = 30;
  protected readonly isLoading = signal(false);

  protected readonly action = signal('');
  protected readonly actor = signal('');
  protected readonly from = signal('');
  protected readonly to = signal('');

  ngOnInit(): void {
    this.load();
  }

  protected apply(): void {
    this.page.set(1);
    this.load();
  }

  protected goToPage(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected load(): void {
    this.isLoading.set(true);
    this.api
      .getAudit({
        page: this.page(),
        pageSize: this.pageSize,
        action: this.action(),
        actor: this.actor().trim(),
        from: this.from() ? new Date(this.from()).toISOString() : '',
        to: this.to() ? new Date(this.to() + 'T23:59:59').toISOString() : ''
      })
      .subscribe({
        next: result => {
          this.entries.set(result.items);
          this.totalCount.set(result.totalCount);
          this.isLoading.set(false);
        },
        error: () => this.isLoading.set(false)
      });
  }
}
