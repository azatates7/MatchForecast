import { Component, input } from '@angular/core';
import type { LineProbability, StatProjection } from '../core/types';

@Component({
  selector: 'app-stats-table',
  templateUrl: './stats-table.html',
})
export class StatsTable {
  readonly stats = input.required<StatProjection[]>();

  /* Kırmızı kartta tek çizgi (0.5) "Var/Yok" olarak okunur; diğer metriklerde "Üst/Alt". */
  protected formatLine(metric: StatProjection['metric'], l: LineProbability): string {
    if (metric === 'RedCards') return `Var %${l.over} · Yok %${l.under}`;
    return `${l.line.toFixed(1)}: Üst %${l.over} · Alt %${l.under}`;
  }
}
