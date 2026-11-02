import { Injectable, signal } from '@angular/core';
import { getLang, loc, locale, pct, setLang, subscribeLang, t, type Lang, type LocalizedText, type Params } from './i18n';
import type { MessageKey } from './messages';

/*
 * i18n.ts'teki dil durumunun Angular sarmalayıcısı. Şablonlarda: {{ i18n.t('key') }}.
 * Metotlar lang() sinyalini okuduğu için dil değişince onları kullanan şablonlar ve computed'lar yeniden hesaplanır
 * (zoneless change detection sinyal değişikliğiyle tetiklenir).
 */
@Injectable({ providedIn: 'root' })
export class I18nService {
  private readonly current = signal<Lang>(getLang());
  readonly lang = this.current.asReadonly();

  constructor() {
    // Tek örnek (root) ve uygulama boyunca yaşar; aboneliği kaldırmaya gerek yok.
    subscribeLang(() => this.current.set(getLang()));
  }

  setLang(lang: Lang): void {
    setLang(lang);
  }

  t(key: MessageKey, params?: Params): string {
    return t(key, params, this.current());
  }

  pct(value: number): string {
    return pct(value, this.current());
  }

  loc(text: LocalizedText | null | undefined): string {
    return loc(text, this.current());
  }

  locale(): string {
    return locale(this.current());
  }
}
