import { Component, computed, inject, input, output } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import type { MatchSummary } from '../core/types';
import { isLive, minuteLabel } from '../core/match-status';

@Component({
  selector: 'app-match-list',
  templateUrl: './match-list.html',
})
export class MatchList {
  readonly matches = input.required<MatchSummary[]>();
  readonly loading = input(false);
  readonly error = input<string>();
  readonly selectedId = input<number | null>(null);
  /** Listenin API'den çekildiği an (ms); canlı dakikayı ilerletmek için */
  readonly fetchedAt = input.required<number>();
  /** Ekrandaki saat (ms); App tarafından periyodik güncellenir */
  readonly now = input.required<number>();

  /** Liste boşken gösterilecek metin (misafir gün/lig seçemediği için ona özel metin verilir) */
  readonly emptyText = input<string>();
  readonly select = output<number>();

  protected readonly i18n = inject(I18nService);

  // Lig bazında grupla, sıralama API'den gelen başlama saatine göre korunur
  protected readonly groups = computed(() => {
    const groups = new Map<string, MatchSummary[]>();
    for (const m of this.matches()) {
      const key = `${m.country} / ${m.league}`;
      groups.set(key, [...(groups.get(key) ?? []), m]);
    }
    return [...groups].map(([league, items]) => ({ league, items }));
  });

  protected readonly isLive = isLive;

  // minuteLabel i18n.ts'teki o anki dili kullanır; lang() okunur ki dil değişince etiket yeniden hesaplansın.
  protected minute(m: MatchSummary): string {
    this.i18n.lang();
    return minuteLabel(m, this.fetchedAt(), this.now());
  }

  protected time(iso: string): string {
    return new Date(iso).toLocaleTimeString(this.i18n.locale(), { hour: '2-digit', minute: '2-digit' });
  }
}