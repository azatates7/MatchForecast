import { Component, inject, signal } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { getAiUsage } from '../core/api';
import type { AiUsageReport } from '../core/types';

/*
 * AI kullanımı sekmesi (ai-usage.view; Admin örtük olarak sahip). Seçilen dönemin toplamları ve
 * gün/sağlayıcı/model kırılımı salt okunurdur. Asıl yetki kontrolü API'dedir (GET /api/ai-usage).
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

  // Küçük tutarlar ($0,0012 gibi) yuvarlanıp 0 görünmesin diye 4 ondalık.
  protected usd(n: number): string {
    return n.toLocaleString(this.i18n.locale(), { style: 'currency', currency: 'USD', minimumFractionDigits: 2, maximumFractionDigits: 4 });
  }

  protected date(d: string): string {
    return new Date(`${d}T00:00:00`).toLocaleDateString(this.i18n.locale());
  }

  protected seconds(ms: number): string {
    return this.num(Math.round(ms / 100) / 10);
  }
}