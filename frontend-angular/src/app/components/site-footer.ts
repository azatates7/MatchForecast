import { Component, inject } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';

/*
 * Sitenin ortak alt bilgisi: her ekranda aynı (giriş, maç seçilmemiş, maç yok, tahmin, yönetim). Tek satır; dar ekranda
 * iki satıra iner. Maça özel bilgi (tahminin neye dayandığı) tahmin panelinin kendi dipnotunda kalır.
 * React karşılığı: frontend/src/components/SiteFooter.tsx
 */
@Component({
  selector: 'app-site-footer',
  host: { style: 'display: contents' },
  template: `
    <footer class="site-foot">
      <p>
        <span class="age-badge" [attr.title]="i18n.t('footer.age')">18+</span>
        <span>{{ i18n.t('footer.notice') }}</span>
      </p>
      <p>{{ i18n.t('footer.copyright', { year }) }}</p>
    </footer>
  `,
})
export class SiteFooter {
  protected readonly i18n = inject(I18nService);
  protected readonly year = new Date().getFullYear();
}