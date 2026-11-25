import { useCallback, useEffect, useState } from 'react'
import { useI18n } from '../../i18n/useI18n'
import { getAiUsage } from '../../api'
import type { AiUsageReport } from '../../types'

const RANGES = [7, 30, 90] as const

/*
 * AI kullanımı sekmesi (ai-usage.view; Admin örtük olarak sahip). Seçilen dönemin toplamları ve
 * gün/sağlayıcı/model kırılımı salt okunurdur. Asıl yetki kontrolü API'dedir (GET /api/ai-usage).
 */
export default function UsageTab() {
  const { t, locale } = useI18n()
  const [days, setDays] = useState<number>(30)
  const [report, setReport] = useState<AiUsageReport | null>(null)
  const [state, setState] = useState<{ loading: boolean; error?: string }>({ loading: true })

  const reload = useCallback(async () => {
    setState({ loading: true })
    try {
      setReport(await getAiUsage(days))
      setState({ loading: false })
    } catch (e) {
      setState({ loading: false, error: (e as Error).message })
    }
  }, [days])

  useEffect(() => { reload() }, [reload])

  const num = (n: number) => n.toLocaleString(locale)
  // Küçük tutarlar ($0,0012 gibi) yuvarlanıp 0 görünmesin diye 4 ondalık.
  const usd = (n: number) => n.toLocaleString(locale, { style: 'currency', currency: 'USD', minimumFractionDigits: 2, maximumFractionDigits: 4 })
  const date = (d: string) => new Date(`${d}T00:00:00`).toLocaleDateString(locale)

  return (
    <>
      <p className="admin-hint admin-note admin-intro">{t('admin.usage.intro')}</p>
      <div className="admin-toolbar">
        <label className="admin-muted">
          {t('admin.usage.range')}{' '}
          <select value={days} onChange={e => setDays(Number(e.target.value))}>
            {RANGES.map(n => <option key={n} value={n}>{t('admin.usage.days', { n })}</option>)}
          </select>
        </label>
        <button type="button" className="cache-btn" onClick={reload} disabled={state.loading}>{t('admin.usage.refresh')}</button>
      </div>

      {state.loading && <p className="admin-placeholder pulse">{t('admin.loading')}</p>}
      {state.error && <p className="note-error" role="alert">{state.error}</p>}

      {!state.loading && !state.error && report && (
        <>
          <dl className="usage-stats">
            <div><dt>{t('admin.usage.calls')}</dt><dd>{num(report.totals.calls)}</dd></div>
            <div><dt>{t('admin.usage.failures')}</dt><dd>{num(report.totals.failures)}</dd></div>
            <div><dt>{t('admin.usage.inputTokens')}</dt><dd>{num(report.totals.inputTokens)}</dd></div>
            <div><dt>{t('admin.usage.outputTokens')}</dt><dd>{num(report.totals.outputTokens)}</dd></div>
            <div><dt>{t('admin.usage.cost')}</dt><dd>{usd(report.totals.estimatedCostUsd)}</dd></div>
            <div><dt>{t('admin.usage.avgDuration')}</dt><dd>{num(Math.round(report.totals.averageDurationMs / 100) / 10)} s</dd></div>
          </dl>

          {report.daily.length === 0 ? <p className="admin-placeholder">{t('admin.usage.empty')}</p> : (
            <div className="admin-table-wrap">
              <table className="admin-table">
                <thead>
                  <tr>
                    <th>{t('admin.usage.col.date')}</th>
                    <th>{t('admin.usage.col.provider')}</th>
                    <th>{t('admin.usage.col.model')}</th>
                    <th className="usage-num">{t('admin.usage.calls')}</th>
                    <th className="usage-num">{t('admin.usage.failures')}</th>
                    <th className="usage-num">{t('admin.usage.inputTokens')}</th>
                    <th className="usage-num">{t('admin.usage.outputTokens')}</th>
                    <th className="usage-num">{t('admin.usage.cost')}</th>
                  </tr>
                </thead>
                <tbody>
                  {report.daily.map(d => (
                    <tr key={`${d.date}|${d.provider}|${d.model}`}>
                      <td className="admin-nowrap">{date(d.date)}</td>
                      <td>{d.provider}</td>
                      <td>{d.model}</td>
                      <td className="usage-num">{num(d.calls)}</td>
                      <td className="usage-num">{num(d.failures)}</td>
                      <td className="usage-num">{num(d.inputTokens)}</td>
                      <td className="usage-num">{num(d.outputTokens)}</td>
                      <td className="usage-num">{usd(d.estimatedCostUsd)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </>
  )
}