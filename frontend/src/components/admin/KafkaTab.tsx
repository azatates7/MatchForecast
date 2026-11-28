import { useCallback, useEffect, useState } from 'react'
import { useI18n } from '../../i18n/useI18n'
import { dismissKafkaDlq, getKafkaDlq, purgeKafkaDlq, replayAllKafkaDlq, replayKafkaDlq } from '../../api'
import type { KafkaDlqMessage, KafkaDlqReplayResult, KafkaDlqReport } from '../../types'
import { ActionStatus, useAction } from './AdminUI'

/*
 * Kafka DLQ sekmesi (kafka.manage; Admin örtük olarak sahip). Veritabanına kaydedilemeyen tahmin mesajlarını hata nedeniyle listeler; tek tek veya topluca yeniden işler (mesaj ana topic'e geri gönderilir, consumer tekrar kaydeder).
 * Sil: kayıt listeden kaldırılır (Kafka tek mesaj silmez); Tümünü sil: DLQ Kafka'dan kalıcı olarak boşaltılır.
 * Okuma DLQ'yu değiştirmez. Asıl yetki kontrolü API'dedir (GET/POST /api/admin/kafka/dlq).
 * Angular karşılığı: frontend-angular/src/app/components/admin-kafka-tab.ts
 */
export default function KafkaTab() {
  const { t, locale } = useI18n()
  const [report, setReport] = useState<KafkaDlqReport | null>(null)
  const [state, setState] = useState<{ loading: boolean; error?: string }>({ loading: true })
  const action = useAction()

  const reload = useCallback(async () => {
    setState({ loading: true })
    try {
      setReport(await getKafkaDlq())
      setState({ loading: false })
    } catch (e) {
      setState({ loading: false, error: (e as Error).message })
    }
  }, [])

  useEffect(() => { reload() }, [reload])

  const num = (n: number) => n.toLocaleString(locale)
  const time = (iso: string) => new Date(iso).toLocaleString(locale, { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })
  const resultText = (r: KafkaDlqReplayResult) =>
    t('kafka.result', { replayed: num(r.replayed), skipped: num(r.skipped), failed: num(r.failed) })

  // Sonuç metni gösterilir; ardından liste yenilenir ki "yeniden işlendi" durumu ve yeni DLQ kayıtları
  // (tekrar başarısız olanlar) görünsün.
  const [info, setInfo] = useState<string | null>(null)
  const runReplay = (call: () => Promise<KafkaDlqReplayResult>) => {
    setInfo(null)
    action.run(async () => {
      const r = await call()
      setInfo(resultText(r))
      await reload()
    })
  }
  const replay = (m: KafkaDlqMessage) => runReplay(() => replayKafkaDlq(m.partition, m.offset))
  const replayAll = () => {
    if (!report || !window.confirm(t('kafka.confirmAll', { n: num(report.pending) }))) return
    runReplay(replayAllKafkaDlq)
  }
  const runAndReload = (call: () => Promise<string>) => {
    setInfo(null)
    action.run(async () => {
      setInfo(await call())
      await reload()
    })
  }
  const dismiss = (m: KafkaDlqMessage) => {
    if (!window.confirm(t('kafka.confirmDismiss'))) return
    runAndReload(async () => { await dismissKafkaDlq(m.partition, m.offset); return t('kafka.dismissed') })
  }
  const purge = () => {
    if (!report || !window.confirm(t('kafka.confirmPurge', { n: num(report.total) }))) return
    runAndReload(async () => t('kafka.purged', { n: num((await purgeKafkaDlq()).deleted) }))
  }

  return (
    <>
      <p className="admin-hint admin-note admin-intro">{t('kafka.intro')}</p>
      <div className="admin-toolbar">
        <button type="button" className="cache-btn" onClick={reload} disabled={state.loading || action.busy}>{t('admin.usage.refresh')}</button>
        {report?.enabled && report.pending > 0 && (
          <button type="button" className="cache-btn" onClick={replayAll} disabled={state.loading || action.busy}>{t('kafka.replayAll')}</button>
        )}
        {report?.enabled && report.total > 0 && (
          <button type="button" className="cache-btn danger-btn" onClick={purge} disabled={state.loading || action.busy}>{t('kafka.purge')}</button>
        )}
      </div>
      <ActionStatus state={action} />
      {info && !action.error && <p className="auth-info admin-status" role="status">{info}</p>}

      {state.loading && <p className="admin-placeholder pulse">{t('admin.loading')}</p>}
      {state.error && <p className="note-error" role="alert">{state.error}</p>}

      {!state.loading && !state.error && report && (!report.enabled ? (
        <p className="admin-placeholder">{t('kafka.disabled')}</p>
      ) : report.total === 0 ? (
        <p className="admin-placeholder">{t('kafka.empty')}</p>
      ) : (
        <>
          <p className="admin-muted">
            {t('kafka.summary', { total: num(report.total), pending: num(report.pending) })}
            {report.truncated && <> · {t('kafka.truncated', { n: num(report.messages.length) })}</>}
          </p>
          <div className="admin-table-wrap">
            <table className="admin-table">
              <thead>
                <tr>
                  <th>{t('kafka.col.time')}</th>
                  <th>{t('kafka.col.match')}</th>
                  <th>{t('kafka.col.error')}</th>
                  <th>{t('kafka.col.status')}</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {report.messages.map(m => (
                  <tr key={`${m.partition}:${m.offset}`}>
                    <td>{time(m.timestamp)}</td>
                    <td>
                      {m.match ?? t('kafka.broken')}
                      <div className="match-id">#{m.fixtureId}{m.kickoff ? ` · ${time(m.kickoff)}` : ''} · p{m.partition}/{m.offset}</div>
                      {m.preview && <div className="dlq-preview">{m.preview}</div>}
                    </td>
                    <td><span className="dlq-error">{m.error ?? '–'}</span></td>
                    <td>
                      {m.replayedAt
                        ? <span className="dlq-status-done" title={time(m.replayedAt)}>{t('kafka.replayed')}</span>
                        : <span className="dlq-status-pending">{t('kafka.pending')}</span>}
                    </td>
                    <td>
                      <div className="dlq-actions">
                        {!m.replayedAt && (
                          <button type="button" className="cache-btn" onClick={() => replay(m)} disabled={action.busy}>
                            {t('kafka.replay')}
                          </button>
                        )}
                        <button type="button" className="cache-btn danger-btn" onClick={() => dismiss(m)} disabled={action.busy}>
                          {t('kafka.dismiss')}
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      ))}
    </>
  )
}