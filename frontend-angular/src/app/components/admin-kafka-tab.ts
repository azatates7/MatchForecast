import { Component, inject, signal } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { getKafkaDlq, replayAllKafkaDlq, replayKafkaDlq } from '../core/api';
import type { KafkaDlqMessage, KafkaDlqReplayResult, KafkaDlqReport } from '../core/types';
import { idle, runAction, type ActionState } from './admin-action';

/*
 * Kafka DLQ sekmesi (kafka.manage; Admin örtük olarak sahip). Veritabanına kaydedilemeyen tahmin mesajlarını hata
 * nedeniyle listeler; tek tek veya topluca yeniden işler (mesaj ana topic'e geri gönderilir, consumer tekrar kaydeder).
 * Okuma DLQ'yu değiştirmez. Asıl yetki kontrolü API'dedir (GET/POST /api/admin/kafka/dlq).
 * React karşılığı: frontend/src/components/admin/KafkaTab.tsx
 */
@Component({
  selector: 'app-kafka-tab',
  templateUrl: './admin-kafka-tab.html',
})
export class KafkaTab {
  protected readonly i18n = inject(I18nService);

  protected readonly report = signal<KafkaDlqReport | null>(null);
  protected readonly state = signal<{ loading: boolean; error?: string }>({ loading: true });
  protected readonly action = signal<ActionState>(idle());

  constructor() {
    this.reload();
  }

  protected async reload() {
    this.state.set({ loading: true });
    try {
      this.report.set(await getKafkaDlq());
      this.state.set({ loading: false });
    } catch (e) {
      this.state.set({ loading: false, error: (e as Error).message });
    }
  }

  protected num(n: number): string {
    return n.toLocaleString(this.i18n.locale());
  }

  protected time(iso: string): string {
    return new Date(iso).toLocaleString(this.i18n.locale(), { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
  }

  // Sonuç metni gösterilir; ardından liste yenilenir ki "yeniden işlendi" durumu ve yeni DLQ kayıtları
  // (tekrar başarısız olanlar) görünsün.
  private runReplay(call: () => Promise<KafkaDlqReplayResult>) {
    let info: string | undefined;
    runAction(this.action, async () => {
      const r = await call();
      info = this.i18n.t('kafka.result', { replayed: this.num(r.replayed), skipped: this.num(r.skipped), failed: this.num(r.failed) });
      await this.reload();
    }).then(() => {
      if (info && !this.action().error) this.action.set({ busy: false, info });
    });
  }

  protected replay(m: KafkaDlqMessage) {
    this.runReplay(() => replayKafkaDlq(m.partition, m.offset));
  }

  protected replayAll() {
    const report = this.report();
    if (!report || !window.confirm(this.i18n.t('kafka.confirmAll', { n: this.num(report.pending) }))) return;
    this.runReplay(replayAllKafkaDlq);
  }
}