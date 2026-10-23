import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Severity, ToastService } from '@masidbaha/shared';
import { AdminApiService } from '../../core/api/admin-api.service';
import { SettingsUpdate, SettingsView } from '../../core/api/admin.models';
import { SEVERITY_NAME, problemMessage } from '../../core/util';
import { ConfirmService } from '../../ui/confirm.service';

@Component({
  selector: 'app-settings-page',
  imports: [FormsModule],
  templateUrl: './settings-page.component.html'
})
export class SettingsPageComponent implements OnInit {
  private readonly api = inject(AdminApiService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  protected readonly severityName = SEVERITY_NAME;
  protected readonly severities: Severity[] = [1, 2, 3, 4];

  protected readonly view = signal<SettingsView | null>(null);
  protected readonly form = signal<SettingsUpdate | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly saving = signal(false);

  ngOnInit(): void {
    this.api.getSettings().subscribe(view => this.show(view));
  }

  protected isOverridden(key: string): boolean {
    return this.view()?.overridden.includes(key) ?? false;
  }

  // Edits one field of the form
  protected set<K extends keyof SettingsUpdate>(key: K, value: SettingsUpdate[K]): void {
    this.form.update(form => (form ? { ...form, [key]: value } : form));
  }

  protected save(): void {
    const form = this.form();
    if (!form || this.saving()) return;

    this.saving.set(true);
    this.error.set(null);
    this.api.saveSettings(form).subscribe({
      next: view => {
        this.saving.set(false);
        this.show(view);
        this.toast.success('Settings saved. They apply right away.');
      },
      error: err => {
        this.saving.set(false);
        this.error.set(problemMessage(err, 'Could not save the settings.'));
      }
    });
  }

  protected async reset(): Promise<void> {
    const confirmed = await this.confirm.ask({
      title: 'Go back to the defaults?',
      message: 'All changes made here are dropped and the values from the server configuration are used again.',
      confirmLabel: 'Reset',
      danger: true
    });
    if (!confirmed) return;

    this.api.resetSettings().subscribe({
      next: view => {
        this.show(view);
        this.toast.success('Defaults restored.');
      },
      error: err => this.toast.error(problemMessage(err))
    });
  }

  private show(view: SettingsView): void {
    this.view.set(view);
    const { version: _version, ...editable } = view.current;
    this.form.set(editable);
  }
}
