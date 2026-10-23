import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '@masidbaha/shared';
import { AdminApiService } from '../../core/api/admin-api.service';
import { AdminAuthService } from '../../core/api/admin-auth.service';
import { ADMIN_ROLES, AdminRole, AdminUserAccount, UserCredential } from '../../core/api/admin.models';
import { problemMessage } from '../../core/util';
import { ConfirmService } from '../../ui/confirm.service';
import { PagerComponent } from '../../ui/pager.component';

@Component({
  selector: 'app-users-page',
  imports: [FormsModule, DatePipe, PagerComponent],
  templateUrl: './users-page.component.html'
})
export class UsersPageComponent implements OnInit {
  private readonly api = inject(AdminApiService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  protected readonly auth = inject(AdminAuthService);

  protected readonly roles = ADMIN_ROLES;

  protected readonly users = signal<AdminUserAccount[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = 25;
  protected readonly search = signal('');

  // New account form, and the temporary password shown once after create/reset
  protected readonly email = signal('');
  protected readonly displayName = signal('');
  protected readonly role = signal<AdminRole>('Viewer');
  protected readonly formError = signal<string | null>(null);
  protected readonly credential = signal<UserCredential | null>(null);

  ngOnInit(): void {
    this.load();
  }

  protected applySearch(): void {
    this.page.set(1);
    this.load();
  }

  protected goToPage(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected load(): void {
    this.api.getUsers(this.page(), this.pageSize, this.search().trim()).subscribe(result => {
      this.users.set(result.items);
      this.totalCount.set(result.totalCount);
    });
  }

  protected isMe(user: AdminUserAccount): boolean {
    return user.id === this.auth.user()?.id;
  }

  protected create(): void {
    this.formError.set(null);
    this.api.createUser(this.email().trim(), this.displayName().trim(), this.role()).subscribe({
      next: credential => {
        this.credential.set(credential);
        this.email.set('');
        this.displayName.set('');
        this.role.set('Viewer');
        this.load();
      },
      error: err => this.formError.set(problemMessage(err, 'Could not create the account.'))
    });
  }

  protected changeRole(user: AdminUserAccount, role: AdminRole): void {
    if (role === user.role) return;

    this.api.updateUser(user.id, { role }).subscribe({
      next: () => {
        this.toast.success(`${user.email} is now ${role}.`);
        this.load();
      },
      error: err => {
        this.toast.error(problemMessage(err));
        this.load(); // put the selector back to the real value
      }
    });
  }

  protected async deactivate(user: AdminUserAccount): Promise<void> {
    const confirmed = await this.confirm.ask({
      title: `Deactivate ${user.email}?`,
      message: 'They are signed out at once and cannot sign in until you reactivate the account.',
      confirmLabel: 'Deactivate',
      danger: true
    });
    if (!confirmed) return;

    this.run(this.api.deactivateUser(user.id), 'Account deactivated.');
  }

  protected reactivate(user: AdminUserAccount): void {
    this.run(this.api.reactivateUser(user.id), 'Account reactivated.');
  }

  protected async resetPassword(user: AdminUserAccount): Promise<void> {
    const confirmed = await this.confirm.ask({
      title: `Reset the password for ${user.email}?`,
      message: 'They are signed out everywhere and must choose a new password at their next sign-in.',
      confirmLabel: 'Reset password',
      danger: true
    });
    if (!confirmed) return;

    this.api.resetPassword(user.id).subscribe({
      next: credential => {
        this.credential.set(credential);
        this.load();
      },
      error: err => this.toast.error(problemMessage(err))
    });
  }

  protected async resetTwoFactor(user: AdminUserAccount): Promise<void> {
    const confirmed = await this.confirm.ask({
      title: `Turn off two-factor for ${user.email}?`,
      message: 'Use this when they have lost their phone and recovery codes. They can set it up again afterwards.',
      confirmLabel: 'Turn off',
      danger: true
    });
    if (!confirmed) return;

    this.run(this.api.resetTwoFactor(user.id), 'Two-factor turned off.');
  }

  protected dismissCredential(): void {
    this.credential.set(null);
  }

  private run(call: ReturnType<AdminApiService['deactivateUser']>, success: string): void {
    call.subscribe({
      next: () => {
        this.toast.success(success);
        this.load();
      },
      error: err => this.toast.error(problemMessage(err))
    });
  }
}
