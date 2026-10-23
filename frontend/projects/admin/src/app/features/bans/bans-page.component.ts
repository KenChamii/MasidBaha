import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '@masidbaha/shared';
import { AdminApiService } from '../../core/api/admin-api.service';
import { AdminAuthService } from '../../core/api/admin-auth.service';
import { Ban } from '../../core/api/admin.models';
import { problemMessage } from '../../core/util';
import { ConfirmService } from '../../ui/confirm.service';
import { PagerComponent } from '../../ui/pager.component';
import { SkeletonRowComponent } from '../../ui/skeleton-row.component';

@Component({
  selector: 'app-bans-page',
  imports: [FormsModule, DatePipe, PagerComponent, SkeletonRowComponent],
  templateUrl: './bans-page.component.html'
})
export class BansPageComponent implements OnInit {
  // placeholder rows while the table loads
  protected readonly skeletonRows = [1, 2, 3, 4, 5];

  private readonly api = inject(AdminApiService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  protected readonly auth = inject(AdminAuthService);

  protected readonly bans = signal<Ban[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = 25;
  protected readonly filter = signal<'active' | 'all'>('active');
  protected readonly isLoading = signal(false);

  // Ban a session by its id (bans on a report are made from the report page)
  protected readonly newSession = signal('');
  protected readonly newReason = signal('');
  protected readonly newMode = signal<1 | 2>(2);
  protected readonly formError = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  protected setFilter(filter: 'active' | 'all'): void {
    this.filter.set(filter);
    this.page.set(1);
    this.load();
  }

  protected goToPage(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected load(): void {
    this.isLoading.set(true);
    this.api.getBans(this.page(), this.pageSize, this.filter() === 'active' ? true : null).subscribe({
      next: result => {
        this.bans.set(result.items);
        this.totalCount.set(result.totalCount);
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    });
  }

  protected addSessionBan(): void {
    this.formError.set(null);
    this.api
      .createBan({
        kind: 1,
        mode: this.newMode(),
        value: this.newSession().trim(),
        reason: this.newReason().trim(),
        hideExistingReports: true
      })
      .subscribe({
        next: () => {
          this.toast.success('Session banned.');
          this.newSession.set('');
          this.newReason.set('');
          this.load();
        },
        error: err => this.formError.set(problemMessage(err, 'Could not create the ban.'))
      });
  }

  protected async lift(ban: Ban): Promise<void> {
    const confirmed = await this.confirm.ask({
      title: 'Lift this ban?',
      message: 'The reporter can post and vote again straight away.',
      confirmLabel: 'Lift ban'
    });
    if (!confirmed) return;

    this.api.liftBan(ban.id).subscribe({
      next: () => {
        this.toast.success('Ban lifted.');
        this.load();
      },
      error: err => this.toast.error(problemMessage(err))
    });
  }
}
