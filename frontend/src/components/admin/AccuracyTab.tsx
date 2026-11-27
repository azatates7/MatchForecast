import { useCallback, useEffect, useState } from 'react'
import { useI18n } from '../../i18n/useI18n'
import { getAccuracy } from '../../api'
import type { AccuracyReport, ResultOutcome } from '../../types'
import type { MessageKey } from '../../i18n/messages'

const RANGES = [7, 30, 90, 180] as const

const OUTCOME_LABEL: Record<ResultOutcome, MessageKey> = {
  Home: 'accuracy.outcome.Home',
  Draw: 'accuracy.outcome.Draw',
  Away: 'accuracy.outcome.Away'
}

/*
 * Tahmin isabeti sekmesi (accuracy.view; Admin örtük olarak sahip). Salt okunur; hesap API'dedir (GET /api/accuracy).
 * Özet kartları, kalibrasyon (güven aralığına göre gerçekleşme), tahmin türü kırılımı, eksik oyuncu bilgisinin etkisi
 * ve son değerlendirilen maçlar. Angular karşılığı: frontend-angular/src/app/components/admin-accuracy-tab.ts
 */
export default function AccuracyTab() {
  const { t, lang, pct, loc, locale } = useI18n()
  const [days, setDays] = useState<number>(30)
  const [report, setReport] = useState<AccuracyReport | null>(null)
  const [state, setState] = useState<{ loading: boolean; error?: string }>({ loading: true })

  const reload = useCallback(async () => {
    setState({ loading: true })
    try {
      setReport(await getAccuracy(days))
      setState({ loading: false })
    } catch (e) {
      setState({ loading: false, error: (e as Error).message })
    }
  }, [days])

  useEffect(() => { reload() }, [reload])

  const num = (n: number) => n.toLocaleString(locale)
  // 66.7 → "%66,7" (tr) / "66.7%" (en); örnek yoksa "–".
  const rateText = (v: number | null) => {
    if (v === null) return '–'
    const n = v.toLocaleString(locale, { maximumFractionDigits: 1 })
    return lang === 'en' ? `${n}%` : `%${n}`
  }
  const brier = (v: number | null) => (v === null ? '–' : v.toLocaleString(locale, { minimumFractionDigits: 3, maximumFractionDigits: 3 }))
  // Gerçekleşme − ortalama güven (puan); negatif = model fazla iddialı.
  const diff = (hitRate: number | null, confidence: number | null) => {
    if (hitRate === null || confidence === null) return { text: '–', cls: '' }
    const d = Math.round((hitRate - confidence) * 10) / 10
    return { text: `${d > 0 ? '+' : ''}${d.toLocaleString(locale)}`, cls: Math.abs(d) < 5 ? '' : d < 0 ? 'acc-neg' : 'acc-pos' }
  }
  const date = (d: string) => new Date(d).toLocaleDateString(locale, { day: '2-digit', month: '2-digit' })

  return (
    <>
      <p className="admin-hint admin-note admin-intro">{t('accuracy.intro')}</p>
      <div className="admin-toolbar">
        <label className="admin-muted usage-range">
          {t('admin.usage.range')}
          <select value={days} onChange={e => setDays(Number(e.target.value))}>
            {RANGES.map(n => <option key={n} value={n}>{t('admin.usage.days', { n })}</option>)}
          </select>
        </label>
        <button type="button" className="cache-btn" onClick={reload} disabled={state.loading}>{t('admin.usage.refresh')}</button>
      </div>

      {state.loading && <p className="admin-placeholder pulse">{t('admin.loading')}</p>}
      {state.error && <p className="note-error" role="alert">{state.error}</p>}

      {!state.loading && !state.error && report && (report.totals.evaluated === 0 ? (
        <>
          <dl className="usage-stats">
            <div><dt>{t('accuracy.pendingVoid')}</dt><dd>{num(report.totals.pending)} / {num(report.totals.void)}</dd></div>
          </dl>
          <p className="admin-placeholder">{t('accuracy.empty')}</p>
        </>
      ) : (
        <>
          <dl className="usage-stats">
            <div>
              <dt>{t('accuracy.evaluated')}</dt>
              <dd>{num(report.totals.evaluated)}</dd>
              <dd className="acc-sub">{t('accuracy.evaluatedOf', { n: num(report.totals.forecasts) })}</dd>
            </div>
            <div>
              <dt>{t('accuracy.resultAccuracy')}</dt>
              <dd>{rateText(report.totals.resultAccuracy)}</dd>
              <dd className="acc-sub">{t('accuracy.hitsOf', { hits: num(report.totals.resultHits), total: num(report.totals.evaluated) })}</dd>
            </div>
            <div>
              <dt>{t('accuracy.brier')}</dt>
              <dd>{brier(report.totals.brierScore)}</dd>
              <dd className="acc-sub">{t('accuracy.brierHint')}</dd>
            </div>
            <div>
              <dt>{t('accuracy.pickRate')}</dt>
              <dd>{rateText(report.totals.pickHitRate)}</dd>
              <dd className="acc-sub">{t('accuracy.hitsOf', { hits: num(report.totals.pickHits), total: num(report.totals.picks) })}</dd>
            </div>
            <div><dt>{t('accuracy.avgConfidence')}</dt><dd>{rateText(report.totals.averageConfidence)}</dd></div>
            <div><dt>{t('accuracy.pendingVoid')}</dt><dd>{num(report.totals.pending)} / {num(report.totals.void)}</dd></div>
          </dl>

          <section className="acc-section">
            <h3>{t('accuracy.calibration')}</h3>
            <p className="admin-hint">{t('accuracy.calibrationHint')}</p>
            <div className="admin-table-wrap">
              <table className="admin-table">
                <thead>
                  <tr>
                    <th>{t('accuracy.col.range')}</th>
                    <th className="usage-num">{t('accuracy.col.picks')}</th>
                    <th className="usage-num">{t('accuracy.col.hits')}</th>
                    <th className="usage-num">{t('accuracy.col.confidence')}</th>
                    <th className="usage-num">{t('accuracy.col.rate')}</th>
                    <th className="usage-num">{t('accuracy.col.diff')}</th>
                  </tr>
                </thead>
                <tbody>
                  {report.calibration.map(b => {
                    const d = diff(b.hitRate, b.averageConfidence)
                    return (
                      <tr key={b.from}>
                        <td className="admin-nowrap">{pct(b.from)} – {pct(b.to)}</td>
                        <td className="usage-num">{num(b.picks)}</td>
                        <td className="usage-num">{num(b.hits)}</td>
                        <td className="usage-num">{rateText(b.averageConfidence)}</td>
                        <td className="usage-num">{rateText(b.hitRate)}</td>
                        <td className={`usage-num ${d.cls}`}>{d.text}</td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          </section>

          <section className="acc-section">
            <h3>{t('accuracy.markets')}</h3>
            <div className="admin-table-wrap">
              <table className="admin-table">
                <thead>
                  <tr>
                    <th>{t('accuracy.col.market')}</th>
                    <th className="usage-num">{t('accuracy.col.picks')}</th>
                    <th className="usage-num">{t('accuracy.col.hits')}</th>
                    <th className="usage-num">{t('accuracy.col.confidence')}</th>
                    <th className="usage-num">{t('accuracy.col.rate')}</th>
                  </tr>
                </thead>
                <tbody>
                  {report.markets.map(m => (
                    <tr key={m.market}>
                      <td>{loc(m.label)}</td>
                      <td className="usage-num">{num(m.picks)}</td>
                      <td className="usage-num">{num(m.hits)}</td>
                      <td className="usage-num">{rateText(m.averageConfidence)}</td>
                      <td className="usage-num">{rateText(m.hitRate)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          <section className="acc-section">
            <h3>{t('accuracy.absences')}</h3>
            <p className="admin-hint">{t('accuracy.absencesHint')}</p>
            <div className="admin-table-wrap">
              <table className="admin-table">
                <thead>
                  <tr>
                    <th>{t('accuracy.col.group')}</th>
                    <th className="usage-num">{t('accuracy.col.matches')}</th>
                    <th className="usage-num">{t('accuracy.col.result')}</th>
                    <th className="usage-num">{t('accuracy.col.pickRate')}</th>
                  </tr>
                </thead>
                <tbody>
                  {report.byAbsences.map(g => (
                    <tr key={g.key}>
                      <td>{t(g.key === 'WithAbsences' ? 'accuracy.group.WithAbsences' : 'accuracy.group.WithoutAbsences')}</td>
                      <td className="usage-num">{num(g.matches)}</td>
                      <td className="usage-num">{rateText(g.resultAccuracy)}</td>
                      <td className="usage-num">{rateText(g.pickHitRate)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          <section className="acc-section">
            <h3>{t('accuracy.recent')}</h3>
            <div className="admin-table-wrap">
              <table className="admin-table">
                <thead>
                  <tr>
                    <th>{t('accuracy.col.date')}</th>
                    <th>{t('accuracy.col.match')}</th>
                    <th>{t('accuracy.col.score')}</th>
                    <th>{t('accuracy.col.predicted')}</th>
                    <th className="usage-num">{t('accuracy.col.picksHit')}</th>
                  </tr>
                </thead>
                <tbody>
                  {report.recent.map(m => (
                    <tr key={m.fixtureId}>
                      <td className="admin-nowrap">{date(m.kickoff)}</td>
                      <td>
                        {m.homeTeam} – {m.awayTeam}
                        <div className="admin-sub">{m.league}</div>
                      </td>
                      <td className="admin-nowrap">{m.score}{m.halfTimeScore && <span className="admin-sub"> ({m.halfTimeScore})</span>}</td>
                      <td className="admin-nowrap">
                        <span className={m.resultHit ? 'acc-hit' : 'acc-miss'} aria-hidden="true">{m.resultHit ? '✓' : '✗'}</span>{' '}
                        {t(OUTCOME_LABEL[m.predictedResult])} {pct(m.predictedProbability)}
                      </td>
                      <td className="usage-num">{num(m.pickHits)} / {num(m.picks)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        </>
      ))}
    </>
  )
}