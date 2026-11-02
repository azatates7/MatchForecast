import { Component, inject } from '@angular/core';
import { LANGS } from '../core/i18n/i18n';
import { I18nService } from '../core/i18n/i18n.service';

/*
 * TR | EN dil seçici (üst bar ve giriş ekranı). Seçili dil vurgulanır; seçim localStorage'da saklanır.
 * Site adı da dile göre değişir: TR -> "Maç Analiz", EN -> "Match Forecast".
 */
@Component({
  selector: 'app-language-toggle',
  template: `
    <div class="lang-toggle" role="group" [attr.aria-label]="i18n.t('lang.label')">
      @for (l of langs; track l) {
        <button
          type="button"
          class="lang-btn"
          [class.is-active]="l === i18n.lang()"
          [attr.aria-pressed]="l === i18n.lang()"
          [attr.title]="i18n.t(l === 'tr' ? 'lang.tr' : 'lang.en')"
          (click)="i18n.setLang(l)"
        >
          {{ l.toUpperCase() }}
        </button>
      }
    </div>
  `,
})
export class LanguageToggle {
  protected readonly i18n = inject(I18nService);
  protected readonly langs = LANGS;
}
