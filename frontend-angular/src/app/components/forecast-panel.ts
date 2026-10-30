import { Component, input, output } from '@angular/core';
import type { ForecastResult, MatchSummary } from '../core/types';
import { StatsTable } from './stats-table';

interface ResultBar {
  title: string;
  label: string;
  parts: { key: string; name: string; value: number; className: string }[];
}

@Component({
  selector: 'app-forecast-panel',
  imports: [StatsTable],
  templateUrl: './forecast-panel.html',
})
export class ForecastPanel {
  readonly match = input<MatchSummary | null>(null);
  readonly forecast = input<ForecastResult | null>(null);
  readonly loading = input(false);
  readonly error = input<string>();
  /** false: "Tekrar / Yeniden analiz et" butonları gizlenir (misafir; API refresh'i yalnızca Admin'e uygular) */
  readonly canRetry = input(true);

  readonly retry = output<void>();

  /*
   * 1 / X / 2 olasılıkları tek çubukta; parçalar toplamı 100 olduğu için yan yana dizilir.
   * Dar parçaya yazı sığmadığı için (%12 altı) çubuğun altında üç değeri de gösteren bir satır her zaman bulunur.
   */
  protected resultBars(f: ForecastResult): ResultBar[] {
    const o = f.outcomes;
    const bar = (title: string, home: number, draw: number, away: number): ResultBar => ({
      title,
      label: `${title}: 1 %${home}, X %${draw}, 2 %${away}`,
      parts: [
        { key: '1', name: 'Ev', value: home, className: 'seg-home' },
        { key: 'X', name: 'Beraberlik', value: draw, className: 'seg-draw' },
        { key: '2', name: 'Deplasman', value: away, className: 'seg-away' },
      ],
    });

    const bars = [bar('Maç sonucu', o.home, o.draw, o.away)];
    if (o.firstHalfHome != null && o.firstHalfDraw != null && o.firstHalfAway != null)
      bars.push(bar('İlk yarı', o.firstHalfHome, o.firstHalfDraw, o.firstHalfAway));
    return bars;
  }
}