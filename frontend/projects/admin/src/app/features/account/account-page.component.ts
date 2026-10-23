import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ToastService } from '@masidbaha/shared';
import { AdminAuthService } from '../../core/api/admin-auth.service';
import { TwoFactorSetup } from '../../core/api/admin.models';
import { problemMessage } from '../../core/util';

@Component({
  selector: 'app-account-page',
  imports: [FormsModule],
  templateUrl: './account-page.component.html'
})
export class AccountPageComponent {
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  protected readonly auth = inject(AdminAuthService);

  // ---- password ----
  protected readonly currentPassword = signal('');
  protected readonly newPassword = signal('');
  protected readonly confirmPassword = signal('');
  protected readonly passwordError = signal<string | null>(null);
  protected readonly savingPassword = signal(false);

  protected readonly passwordsMatch = computed(() => this.newPassword() === this.confirmPassword());

  // ---- two-factor ----
  protected readonly setup = signal<TwoFactorSetup | null>(null);
  protected readonly setupCode = signal('');
  protected readonly recoveryCodes = signal<string[] | null>(null);
  protected readonly disablePassword = signal('');
  protected readonly disableCode = signal('');
  protected readonly twoFactorError = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected changePassword(): void {
    if (this.savingPassword()) return;
    if (!this.passwordsMatch()) {
      this.passwordError.set('The new passwords do not match.');
      return;
    }

    const wasForced = this.auth.mustChangePassword();
    this.savingPassword.set(true);
    this.passwordError.set(null);

    this.auth.changePassword(this.currentPassword(), this.newPassword()).subscribe({
      next: () => {
        this.savingPassword.set(false);
        this.currentPassword.set('');
        this.newPassword.set('');
        this.confirmPassword.set('');
        this.toast.success('Password changed.');
        if (wasForced) void this.router.navigate(['/dashboard']);
      },
      error: err => {
        this.savingPassword.set(false);
        this.passwordError.set(problemMessage(err, 'Could not change the password.'));
      }
    });
  }

  protected startSetup(): void {
    this.busy.set(true);
    this.twoFactorError.set(null);
    this.auth.setupTwoFactor().subscribe({
      next: setup => {
        this.busy.set(false);
        this.setup.set(setup);
      },
      error: err => {
        this.busy.set(false);
        this.twoFactorError.set(problemMessage(err));
      }
    });
  }

  protected confirmSetup(): void {
    if (!this.setupCode().trim() || this.busy()) return;

    this.busy.set(true);
    this.twoFactorError.set(null);
    this.auth.enableTwoFactor(this.setupCode().trim()).subscribe({
      next: codes => {
        this.busy.set(false);
        this.setup.set(null);
        this.setupCode.set('');
        this.recoveryCodes.set(codes);
      },
      error: err => {
        this.busy.set(false);
        this.twoFactorError.set(problemMessage(err, 'That code is not valid.'));
      }
    });
  }

  protected disable(): void {
    if (this.busy()) return;

    this.busy.set(true);
    this.twoFactorError.set(null);
    this.auth.disableTwoFactor(this.disablePassword(), this.disableCode().trim()).subscribe({
      next: () => {
        this.busy.set(false);
        this.disablePassword.set('');
        this.disableCode.set('');
        this.toast.success('Two-factor sign-in is off.');
      },
      error: err => {
        this.busy.set(false);
        this.twoFactorError.set(problemMessage(err));
      }
    });
  }

  protected dismissRecoveryCodes(): void {
    this.recoveryCodes.set(null);
  }
}
