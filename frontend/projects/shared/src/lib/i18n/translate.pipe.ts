import { Pipe, PipeTransform, inject } from '@angular/core';
import { I18nService, MessageParams } from './i18n.service';

// {{ 'map.report' | t }}. Impure on purpose: it must run again when the language changes.
@Pipe({ name: 't', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(key: string, params?: MessageParams): string {
    return this.i18n.t(key, params);
  }
}
