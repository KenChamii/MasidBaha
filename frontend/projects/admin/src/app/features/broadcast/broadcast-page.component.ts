import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '@masidbaha/shared';
import { AdminApiService } from '../../core/api/admin-api.service';
import { AdminAuthService } from '../../core/api/admin-auth.service';
import { Broadcast } from '../../core/api/admin.models';
import { problemMessage } from '../../core/util';
import { ConfirmService } from '../../ui/confirm.service';
import { PagerComponent } from '../../ui/pager.component';

@Component({
  selector: 'app-broadcast-page',
  imports: [FormsModule, DatePipe, PagerComponent],
  templateUrl: './broadcast-page.component.html'
})
export class BroadcastPageComponent implements OnInit {
  private readonly api = inject(AdminApiService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  protected readonly auth = inject(AdminAuthService);

  protected readonly title = signal('');
  protected readonly body = signal('');
  protected readonly url = signal('/map');
  protected readonly error = signal<string | null>(null);
  protected readonly sending = signal(false);

  protected readonly history = signal<Broadcast[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = 10;

  ngOnInit(): void {
    this.load();
  }

  protected goToPage(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected load(): void {
    this.api.getBroadcasts(this.page(), this.pageSize).subscribe(result => {
      this.history.set(result.items);
      this.totalCount.set(result.totalCount);
    });
  }

  protected async send(): Promise<void> {
    if (this.sending()) return;

    const confirmed = await this.confirm.ask({
      title: 'Send to every subscriber?',
      message: 'A push notification goes to everyone who turned alerts on. It cannot be recalled.',
      confirmLabel: 'Send now'
    });
    if (!confirmed) return;

    this.sending.set(true);
    this.error.set(null);

    this.api.sendBroadcast(this.title().trim(), this.body().trim(), this.url().trim()).subscribe({
      next: sent => {
        this.sending.set(false);
        this.toast.success(`Sent to ${sent.recipients} subscriber(s).`);
        this.title.set('');
        this.body.set('');
        this.page.set(1);
        this.load();
      },
      error: err => {
        this.sending.set(false);
        this.error.set(problemMessage(err, 'Could not send the announcement.'));
      }
    });
  }
}
