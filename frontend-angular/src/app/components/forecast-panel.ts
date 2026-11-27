import { Component, inject, input, output, signal } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import type { ForecastResult, MatchSummary } from '../core/types';
import { ABSENCE_LABEL, STRICTNESS_CLASS, STRICTNESS_LABEL, absenceFootnote, groupAbsences, refereeStats, sourceLabel } from '../core/absences';
import { matchUrl } from '../core/match-route';
import { StatsTable } from './stats-table';

interface ResultBar {
  title: string;
  prefix: string;
  label: string;
  parts: { key: string; name: string; value: number; className: string }[];
}

@Component({
  selector: 'app-forecast-panel',
  imports: [StatsTable],
  templateUrl: './forecast-panel.html',
})
export class ForecastPanel {
  // Eksik oyuncular (React: ForecastPanel içindeki Absences); maç günü resmi açıklamalardan, öncesinde "maç günü netleşir".
  protected readonly absenceLabel = ABSENCE_LABEL;
  protected readonly absenceFootnote = absenceFootnote;
  protected readonly sourceLabel = sourceLabel;

  /** true: başlıkta fikstür ID'si ve kopyala butonu (yalnızca Admin) */
  readonly showId = input(false);
  protected readonly copied = signal(false);

  // Panoya kopyalar; clipboard izni yoksa ID yine elle seçilebilir.
  protected copyId(id: number) {
    navigator.clipboard?.writeText(String(id)).then(() => {
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 1500);
    }, () => undefined);
  }
  // Maçın paylaşılabilir adresini (#match/<id>) panoya kopyalar; herkese görünür. Açan kişinin yetkisini API denetler.
  protected readonly linkCopied = signal(false);
  protected copyLink(id: number) {
    navigator.clipboard?.writeText(matchUrl(id)).then(() => {
      this.linkCopied.set(true);
      setTimeout(() => this.linkCopied.set(false), 1500);
    }, () => undefined);
  }

  // Hakem ve kart eğilimi (React: ForecastPanel içindeki Referee). Yalnızca bulunan bilgiler gösterilir.
  protected readonly strictnessLabel = STRICTNESS_LABEL;
  protected readonly strictnessClass = STRICTNESS_CLASS;
  protected readonly refereeStats = refereeStats;

  protected refereeNum(n: number): string {
    return n.toLocaleString(this.i18n.locale(), { maximumFractionDigits: 2 });
  }

  protected absences(f: ForecastResult) {
    return f.absences ? groupAbsences(f.match, f.absences.players) : [];
  }

  protected checkedAt(iso: string): string {
    return new Date(iso).toLocaleTimeString(this.i18n.locale(), { hour: '2-digit', minute: '2-digit' });
  }

  readonly match = input<MatchSummary | null>(null);
  readonly forecast = input<ForecastResult | null>(null);
  readonly loading = input(false);
  readonly error = input<string>();
  /** false: "Tekrar / Yeniden analiz et" butonları gizlenir (misafir; API refresh'i yalnızca Admin'e uygular) */
  readonly canRetry = input(true);

  readonly retry = output<void>();

  protected readonly i18n = inject(I18nService);

  /*
   * 1 / X / 2 olasılıkları tek çubukta; parçalar toplamı 100 olduğu için yan yana dizilir.
   * Dar parçaya yazı sığmadığı için (%12 altı) çubuğun altında üç değeri de gösteren bir satır her zaman bulunur.
   */
  protected resultBars(f: ForecastResult): ResultBar[] {
    const o = f.outcomes;
    const i18n = this.i18n;
    const bar = (title: string, prefix: string, home: number, draw: number, away: number): ResultBar => ({
      title,
      prefix,
      label: `${title}: ${prefix} 1 ${i18n.pct(home)}, ${prefix} X ${i18n.pct(draw)}, ${prefix} 2 ${i18n.pct(away)}`,
      parts: [
        { key: '1', name: i18n.t('outcome.home'), value: home, className: 'seg-home' },
        { key: 'X', name: i18n.t('outcome.draw'), value: draw, className: 'seg-draw' },
        { key: '2', name: i18n.t('outcome.away'), value: away, className: 'seg-away' },
      ],
    });

    const bars = [bar(i18n.t('outcome.fullTime'), i18n.t('outcome.fullTimeShort'), o.home, o.draw, o.away)];
    if (o.firstHalfHome != null && o.firstHalfDraw != null && o.firstHalfAway != null)
      bars.push(bar(i18n.t('outcome.firstHalf'), i18n.t('outcome.firstHalfShort'), o.firstHalfHome, o.firstHalfDraw, o.firstHalfAway));
    return bars;
  }
}