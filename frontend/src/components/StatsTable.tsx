import type { LineProbability, StatProjection } from '../types'

interface Props {
  stats: StatProjection[]
}

/* Kırmızı kartta tek çizgi (0.5) "Var/Yok" olarak okunur; diğer metriklerde "Üst/Alt". */
function formatLine(metric: StatProjection['metric'], l: LineProbability) {
  if (metric === 'RedCards') return `Var %${l.over} · Yok %${l.under}`
  return `${l.line.toFixed(1)}: Üst %${l.over} · Alt %${l.under}`
}

export default function StatsTable({ stats }: Props) {
  if (stats.length === 0) return null

  return (
    <section className="stats" aria-labelledby="stats-title">
      <h3 id="stats-title" className="section-title">Maç istatistikleri tahmini</h3>
      <div className="stats-scroll">
        <table className="stats-table">
          <thead>
            <tr>
              <th scope="col">İstatistik</th>
              <th scope="col" className="num">Beklenen</th>
              <th scope="col" className="num">Aralık</th>
              <th scope="col">Alt / Üst</th>
            </tr>
          </thead>
          <tbody>
            {stats.map(s => (
              <tr key={s.metric}>
                <th scope="row">
                  {s.label}
                  {s.reasoning && <span className="stats-reason">{s.reasoning}</span>}
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
      <p className="stats-note">Aralık, olası değerlerin %80'ini kapsar.</p>
    </section>
  )
}