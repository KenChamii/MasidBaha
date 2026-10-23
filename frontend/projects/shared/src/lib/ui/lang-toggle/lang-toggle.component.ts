import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { I18nService } from '../../i18n/i18n.service';

// Shows the language it will switch to: "FIL" while the page is in English, "EN" while it is in Filipino
@Component({
  selector: 'mb-lang-toggle',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      type="button"
      class="lang-toggle"
      (click)="i18n.toggle()"
      [attr.aria-label]="i18n.lang() === 'en' ? i18n.t('lang.switchToFil') : i18n.t('lang.switchToEn')"
      [title]="i18n.lang() === 'en' ? i18n.t('lang.switchToFil') : i18n.t('lang.switchToEn')"
    >
      {{ i18n.lang() === 'en' ? 'FIL' : 'EN' }}
    </button>
  `,
  styleUrl: './lang-toggle.component.scss'
})
export class LangToggleComponent {
  readonly i18n = inject(I18nService);
}
