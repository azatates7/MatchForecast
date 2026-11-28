import { useCallback, useEffect, useState } from 'react'
import { useI18n } from '../../i18n/useI18n'
import { getAiUsage } from '../../api'
import type { AiUsagePurpose, AiUsageReport, AiUsageSearchMonth, ApiFootballQuota } from '../../types'
import type { MessageKey } from '../../i18n/messages'

const RANGES = [7, 30, 90] as const

const PURPOSE_LABEL: Record<AiUsagePurpose, MessageKey> = {
  Forecast: 'admin.usage.purpose.Forecast',
  Absences: 'admin.usage.purpose.Absences'
}

/*
 * AI kullanımı sekmesi (ai-usage.view; Admin örtük olarak sahip). Seçilen dönemin toplamları ve gün/tür/sağlayıcı/model kırılımı salt okunurdur. Tür: tahmin üretimi veya eksik oyuncu araması (Google araması sayısıyla). Asıl yetki kontrolü API'dedir (GET /api/ai-usage).
 */
/*
 * Bu ayın Google araması sayacı (seçilen dönemden bağımsız). Sınırın %90'ına gelince uyarı, dolunca eksik oyuncu aramasının durduğu söylenir (backend: Absences:MonthlySearchLimit).
 */
function SearchMonth({ month }: { month: AiUsageSearchMonth }) {
  const { t, locale } = useI18n()
  const num = (n: number) => n.toLocaleString(locale)
  const level = month.limit <= 0 ? 'ok' : month.used >= month.limit ? 'full' : month.used >= month.limit * 0.9 ? 'near' : 'ok'
  return (
    <div className={`usage-month${level === 'ok' ? '' : ` is-${level}`}`} role={level === 'ok' ? undefined : 'status'}>
      <p>
        {month.limit > 0
          ? t('admin.usage.month', { used: num(month.used), limit: num(month.limit), free: num(month.freeQuota) })
          : t('admin.usage.monthUnlimited', { used: num(month.used), free: num(month.freeQuota) })}
      </p>
      {level !== 'ok' && <p>{t(level === 'full' ? 'admin.usage.monthFull' : 'admin.usage.monthNear')}</p>}
    </div>
  )
}

/*
 * API-Football günlük istek kotası (son API-Football cevabının başlıklarından). Kalan istek %20'ye inince uyarı, bitince "doldu" uyarısı gösterilir (backend aynı eşikle yöneticiye günde bir kez mail atar: OddsProvider:QuotaWarnPercent).
 */
function ApiFootballQuotaLine({ quota }: { quota: ApiFootballQuota | null }) {
  const { t, locale } = useI18n()
  if (!quota) return <div className="usage-month"><p>{t('admin.usage.apiFootballNone')}</p></div>
  const num = (n: number) => n.toLocaleString(locale)
  const time = new Date(quota.updatedAt).toLocaleString(locale, { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })
  const level = quota.remaining <= 0 ? 'full' : quota.remaining * 100 <= quota.limit * 20 ? 'near' : 'ok'
  return (
    <div className={`usage-month${level === 'ok' ? '' : ` is-${level}`}`} role={level === 'ok' ? undefined : 'status'}>
      <p>{t('admin.usage.apiFootball', { remaining: num(quota.remaining), limit: num(quota.limit), time })}</p>
      {level !== 'ok' && <p>{t(level === 'full' ? 'admin.usage.apiFootballEmpty' : 'admin.usage.apiFootballLow')}</p>}
    </div>
  )
}

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
  // Küçük tutarlar ($0,0012 gibi) yuvarlanıp 0 görünmesin diye sabit 4 ondalık (kolon hizalı kalır); 0 ise $0,00.
  const usd = (n: number) => {
    const digits = n === 0 ? 2 : 4
    return n.toLocaleString(locale, { style: 'currency', currency: 'USD', minimumFractionDigits: digits, maximumFractionDigits: digits })
  }
  const date = (d: string) => new Date(`${d}T00:00:00`).toLocaleDateString(locale)

  return (
    <>
      <p className="admin-hint admin-note admin-intro">{t('admin.usage.intro')}</p>
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

      {!state.loading && !state.error && report && (
        <>
          <SearchMonth month={report.searchMonth} />
          <ApiFootballQuotaLine quota={report.apiFootball ?? null} />
          <dl className="usage-stats">
            <div><dt>{t('admin.usage.calls')}</dt><dd>{num(report.totals.calls)}</dd></div>
            <div><dt>{t('admin.usage.failures')}</dt><dd>{num(report.totals.failures)}</dd></div>
            <div><dt>{t('admin.usage.inputTokens')}</dt><dd>{num(report.totals.inputTokens)}</dd></div>
            <div><dt>{t('admin.usage.outputTokens')}</dt><dd>{num(report.totals.outputTokens)}</dd></div>
            <div><dt>{t('admin.usage.searches')}</dt><dd>{num(report.totals.searchQueries)}</dd></div>
            <div><dt>{t('admin.usage.cost')}</dt><dd>{usd(report.totals.estimatedCostUsd)}</dd></div>
            <div><dt>{t('admin.usage.avgDuration')}</dt><dd>{num(Math.round(report.totals.averageDurationMs / 100) / 10)} s</dd></div>
          </dl>

          {report.daily.length === 0 ? <p className="admin-placeholder">{t('admin.usage.empty')}</p> : (
            <div className="admin-table-wrap">
              <table className="admin-table">
                <thead>
                  <tr>
                    <th>{t('admin.usage.col.date')}</th>
                    <th>{t('admin.usage.col.purpose')}</th>
                    <th>{t('admin.usage.col.provider')}</th>
                    <th>{t('admin.usage.col.model')}</th>
                    <th className="usage-num">{t('admin.usage.calls')}</th>
                    <th className="usage-num">{t('admin.usage.failures')}</th>
                    <th className="usage-num">{t('admin.usage.inputTokens')}</th>
                    <th className="usage-num">{t('admin.usage.outputTokens')}</th>
                    <th className="usage-num">{t('admin.usage.searches')}</th>
                    <th className="usage-num">{t('admin.usage.cost')}</th>
                  </tr>
                </thead>
                <tbody>
                  {report.daily.map(d => (
                    <tr key={`${d.date}|${d.purpose}|${d.provider}|${d.model}`}>
                      <td className="admin-nowrap">{date(d.date)}</td>
                      <td>{PURPOSE_LABEL[d.purpose] ? t(PURPOSE_LABEL[d.purpose]) : d.purpose}</td>
                      <td>{d.provider}</td>
                      <td className="admin-nowrap">{d.model}</td>
                      <td className="usage-num">{num(d.calls)}</td>
                      <td className="usage-num">{num(d.failures)}</td>
                      <td className="usage-num">{num(d.inputTokens)}</td>
                      <td className="usage-num">{num(d.outputTokens)}</td>
                      <td className="usage-num">{num(d.searchQueries)}</td>
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