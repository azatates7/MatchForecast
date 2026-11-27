import { Component, inject, signal } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { getAiUsage } from '../core/api';
import type { AiUsagePurpose, AiUsageReport } from '../core/types';
import type { MessageKey } from '../core/i18n/messages';

const PURPOSE_LABEL: Record<AiUsagePurpose, MessageKey> = {
  Forecast: 'admin.usage.purpose.Forecast',
  Absences: 'admin.usage.purpose.Absences',
};

/*
 * AI kullanımı sekmesi (ai-usage.view; Admin örtük olarak sahip). Seçilen dönemin toplamları ve
 * gün/tür/sağlayıcı/model kırılımı salt okunurdur. Tür: tahmin üretimi veya eksik oyuncu araması (Google araması sayısıyla). Asıl yetki kontrolü API'dedir (GET /api/ai-usage).
 * React karşılığı: frontend/src/components/admin/UsageTab.tsx
 */
@Component({
  selector: 'app-usage-tab',
  templateUrl: './admin-usage-tab.html',
})
export class UsageTab {
  protected readonly i18n = inject(I18nService);
  protected readonly ranges = [7, 30, 90] as const;

  protected readonly days = signal(30);
  protected readonly report = signal<AiUsageReport | null>(null);
  protected readonly state = signal<{ loading: boolean; error?: string }>({ loading: true });

  constructor() {
    this.reload();
  }

  protected setDays(value: string) {
    this.days.set(Number(value));
    this.reload();
  }

  protected async reload() {
    this.state.set({ loading: true });
    try {
      this.report.set(await getAiUsage(this.days()));
      this.state.set({ loading: false });
    } catch (e) {
      this.state.set({ loading: false, error: (e as Error).message });
    }
  }

  protected num(n: number): string {
    return n.toLocaleString(this.i18n.locale());
  }

  // Küçük tutarlar ($0,0012 gibi) yuvarlanıp 0 görünmesin diye sabit 4 ondalık (kolon hizalı kalır); 0 ise $0,00.
  protected usd(n: number): string {
    const digits = n === 0 ? 2 : 4;
    return n.toLocaleString(this.i18n.locale(), { style: 'currency', currency: 'USD', minimumFractionDigits: digits, maximumFractionDigits: digits });
  }

  protected date(d: string): string {
    return new Date(`${d}T00:00:00`).toLocaleDateString(this.i18n.locale());
  }

  protected purpose(p: AiUsagePurpose): string {
    return PURPOSE_LABEL[p] ? this.i18n.t(PURPOSE_LABEL[p]) : p;
  }

  protected seconds(ms: number): string {
    return this.num(Math.round(ms / 100) / 10);
  }
}