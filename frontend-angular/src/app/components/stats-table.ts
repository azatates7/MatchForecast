import { Component, inject, input } from '@angular/core';
import type { LineProbability, StatProjection } from '../core/types';
import { I18nService } from '../core/i18n/i18n.service';

@Component({
  selector: 'app-stats-table',
  templateUrl: './stats-table.html',
})
export class StatsTable {
  readonly stats = input.required<StatProjection[]>();
  protected readonly i18n = inject(I18nService);

  /* Kırmızı kartta tek çizgi (0.5) "Var/Yok" olarak okunur; diğer metriklerde "Üst/Alt". */
  protected formatLine(metric: StatProjection['metric'], l: LineProbability): string {
    const over = this.i18n.pct(l.over);
    const under = this.i18n.pct(l.under);
    if (metric === 'RedCards') return this.i18n.t('stats.yesNo', { over, under });
    return this.i18n.t('stats.line', { line: l.line.toFixed(1), over, under });
  }
}
