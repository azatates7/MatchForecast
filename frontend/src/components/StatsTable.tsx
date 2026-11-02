import type { LineProbability, StatProjection } from '../types'
import { useI18n } from '../i18n/useI18n'
import { pct, t } from '../i18n/i18n'

interface Props {
  stats: StatProjection[]
}

/* Kırmızı kartta tek çizgi (0.5) "Var/Yok" olarak okunur; diğer metriklerde "Üst/Alt". */
function formatLine(metric: StatProjection['metric'], l: LineProbability) {
  const over = pct(l.over)
  const under = pct(l.under)
  if (metric === 'RedCards') return t('stats.yesNo', { over, under })
  return t('stats.line', { line: l.line.toFixed(1), over, under })
}

export default function StatsTable({ stats }: Props) {
  // formatLine i18n.ts'teki o anki dili kullanır; hook, dil değişince tablonun yeniden çizilmesini sağlar.
  const { t, loc } = useI18n()
  if (stats.length === 0) return null

  return (
    <section className="stats" aria-labelledby="stats-title">
      <h3 id="stats-title" className="section-title">{t('stats.title')}</h3>
      <div className="stats-scroll">
        <table className="stats-table">
          <thead>
            <tr>
              <th scope="col">{t('stats.stat')}</th>
              <th scope="col" className="num">{t('stats.expected')}</th>
              <th scope="col" className="num">{t('stats.range')}</th>
              <th scope="col">{t('stats.lines')}</th>
            </tr>
          </thead>
          <tbody>
            {stats.map(s => (
              <tr key={s.metric}>
                <th scope="row">
                  {loc(s.label)}
                  {loc(s.reasoning) && <span className="stats-reason">{loc(s.reasoning)}</span>}
                </th>
                <td className="num">{s.expected.toFixed(1)}</td>
                <td className="num">{s.rangeMin}–{s.rangeMax}</td>
                <td className="stats-lines">
                  {s.lines.length === 0
                    ? <span className="muted">—</span>
                    : s.lines.map(l => <span key={l.line}>{formatLine(s.metric, l)}</span>)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="stats-note">{t('stats.note')}</p>
    </section>
  )
}