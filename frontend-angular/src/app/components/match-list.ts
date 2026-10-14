import { Component, computed, input, output } from '@angular/core';
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

  readonly select = output<number>();

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

  protected minute(m: MatchSummary): string {
    return minuteLabel(m, this.fetchedAt(), this.now());
  }

  protected time(iso: string): string {
    return new Date(iso).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' });
  }
}
