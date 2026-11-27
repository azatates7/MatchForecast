import { Component, inject, signal } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { getAccuracy } from '../core/api';
import type { AccuracyGroupKey, AccuracyReport, ResultOutcome } from '../core/types';
import type { MessageKey } from '../core/i18n/messages';

const OUTCOME_LABEL: Record<ResultOutcome, MessageKey> = {
  Home: 'accuracy.outcome.Home',
  Draw: 'accuracy.outcome.Draw',
  Away: 'accuracy.outcome.Away',
};

const GROUP_LABEL: Record<AccuracyGroupKey, MessageKey> = {
  WithAbsences: 'accuracy.group.WithAbsences',
  WithoutAbsences: 'accuracy.group.WithoutAbsences',
};

/*
 * Tahmin isabeti sekmesi (accuracy.view; Admin örtük olarak sahip). Salt okunur; hesap API'dedir (GET /api/accuracy).
 * Özet kartları, kalibrasyon (güven aralığına göre gerçekleşme), tahmin türü kırılımı, eksik oyuncu bilgisinin etkisi
 * ve son değerlendirilen maçlar. React karşılığı: frontend/src/components/admin/AccuracyTab.tsx
 */
@Component({
  selector: 'app-accuracy-tab',
  templateUrl: './admin-accuracy-tab.html',
})
export class AccuracyTab {
  protected readonly i18n = inject(I18nService);
  protected readonly ranges = [7, 30, 90, 180] as const;

  protected readonly days = signal(30);
  protected readonly report = signal<AccuracyReport | null>(null);
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
      this.report.set(await getAccuracy(this.days()));
      this.state.set({ loading: false });
    } catch (e) {
      this.state.set({ loading: false, error: (e as Error).message });
    }
  }

  protected num(n: number): string {
    return n.toLocaleString(this.i18n.locale());
  }

  // 66.7 → "%66,7" (tr) / "66.7%" (en); örnek yoksa "–".
  protected rate(v: number | null): string {
    if (v === null) return '–';
    const n = v.toLocaleString(this.i18n.locale(), { maximumFractionDigits: 1 });
    return this.i18n.lang() === 'en' ? `${n}%` : `%${n}`;
  }

  protected brier(v: number | null): string {
    return v === null ? '–' : v.toLocaleString(this.i18n.locale(), { minimumFractionDigits: 3, maximumFractionDigits: 3 });
  }

  // Gerçekleşme − ortalama güven (puan); negatif = model fazla iddialı. 5 puandan küçük fark renklendirilmez.
  protected diff(hitRate: number | null, confidence: number | null): { text: string; cls: string } {
    if (hitRate === null || confidence === null) return { text: '–', cls: '' };
    const d = Math.round((hitRate - confidence) * 10) / 10;
    return { text: `${d > 0 ? '+' : ''}${d.toLocaleString(this.i18n.locale())}`, cls: Math.abs(d) < 5 ? '' : d < 0 ? 'acc-neg' : 'acc-pos' };
  }

  protected date(d: string): string {
    return new Date(d).toLocaleDateString(this.i18n.locale(), { day: '2-digit', month: '2-digit' });
  }

  protected outcome(o: ResultOutcome): string {
    return this.i18n.t(OUTCOME_LABEL[o]);
  }

  protected group(k: AccuracyGroupKey): string {
    return this.i18n.t(GROUP_LABEL[k]);
  }
}