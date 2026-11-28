import { Fragment, useState } from 'react'
import type { AbsenceReport, ForecastResult, MatchSummary, OutcomeProbabilities, RefereeInfo } from '../types'
import StatsTable from './StatsTable'
import { useI18n } from '../i18n/useI18n'
import { ABSENCE_LABEL, STRICTNESS_CLASS, STRICTNESS_LABEL, absenceFootnote, groupAbsences, hasAbsenceList, refereeStats, sourceLabel } from '../Absences'
import { matchUrl } from '../MatchRoute'

interface Props {
  match: MatchSummary | null
  forecast: ForecastResult | null
  loading: boolean
  error?: string
  onRetry: () => void
  /** false: "Tekrar / Yeniden analiz et" butonları gizlenir (misafir; API refresh'i yalnızca Admin'e uygular) */
  canRetry?: boolean
}

/*
 * 1 / X / 2 olasılıkları tek çubukta; parçalar toplamı 100 olduğu için yan yana dizilir.
 * Dar parçaya yazı sığmadığı için (%12 altı) çubuğun altında üç değeri de gösteren bir satır her zaman bulunur.
 */
function ResultBar({ title, prefix, home, draw, away }: { title: string; prefix: string; home: number; draw: number; away: number }) {
  const { t, pct } = useI18n()
  const parts = [
    { key: '1', name: t('outcome.home'), value: home, className: 'seg-home' },
    { key: 'X', name: t('outcome.draw'), value: draw, className: 'seg-draw' },
    { key: '2', name: t('outcome.away'), value: away, className: 'seg-away' },
  ]
  return (
    <div className="result">
      <p className="result-title">{title}</p>
      <div className="result-bar" role="img" aria-label={`${title}: ${prefix} 1 ${pct(home)}, ${prefix} X ${pct(draw)}, ${prefix} 2 ${pct(away)}`}>
        {parts.map(p => (
          <span key={p.key} className={`result-seg ${p.className}`} style={{ flexGrow: p.value }}>
            {p.value >= 12 && `${prefix} ${p.key} ${pct(p.value)}`}
          </span>
        ))}
      </div>
      <p className="result-legend" aria-hidden="true">
        {parts.map(p => (
          <span key={p.key} className="legend-item">
            <i className={`legend-dot ${p.className}`} />
            {p.name} <strong>{pct(p.value)}</strong>
          </span>
        ))}
      </p>
    </div>
  )
}

function Outcomes({ o }: { o: OutcomeProbabilities }) {
  const { t, pct } = useI18n()
  return (
    <section className="outcomes">
      <ResultBar title={t('outcome.fullTime')} prefix={t('outcome.fullTimeShort')} home={o.home} draw={o.draw} away={o.away} />
      {o.firstHalfHome != null && o.firstHalfDraw != null && o.firstHalfAway != null && (
        <ResultBar title={t('outcome.firstHalf')} prefix={t('outcome.firstHalfShort')} home={o.firstHalfHome} draw={o.firstHalfDraw} away={o.firstHalfAway} />
      )}
      <p className="btts">
        {t('outcome.btts')} <strong>{t('outcome.yes')} {pct(o.bttsYes)}</strong> · {t('outcome.no')} {pct(100 - o.bttsYes)}
      </p>
    </section>
  )
}

// Hakem ve kart eğilimi (maç günü aramasından). Yalnızca bulunan bilgiler gösterilir; hakem bulunamadıysa bölüm hiç çıkmaz.
function Referee({ referee }: { referee: RefereeInfo }) {
  const { t, loc, locale } = useI18n()
  const stats = refereeStats(referee)
  const num = (n: number) => n.toLocaleString(locale, { maximumFractionDigits: 2 })
  const style = referee.style ? loc(referee.style) : ''
  return (
    <section className="referee">
      <h3 className="section-title">{t('referee.title')}</h3>
      <div className="referee-card">
        <div className="referee-head">
          <span className="referee-name">{referee.name}</span>
          {referee.strictness && (
            <span className={`absence-tag ${STRICTNESS_CLASS[referee.strictness]}`} title={t('referee.strictnessHint')}>
              {t(STRICTNESS_LABEL[referee.strictness])}
            </span>
          )}
        </div>
        {stats.length > 0 && (
          <p className="referee-stats">
            {t('referee.perMatch')}:{' '}
            {stats.map((s, i) => (
              <Fragment key={s.label}>
                {i > 0 && <span className="referee-sep" aria-hidden="true">·</span>}
                {t(s.label, { n: num(s.value) })}
              </Fragment>
            ))}
            {referee.matches !== null && <> <span className="referee-matches">{t('referee.matches', { n: referee.matches })}</span></>}
          </p>
        )}
        {style && <p className="referee-style">{style}</p>}
      </div>
    </section>
  )
}

// Modelin dikkate aldığı eksik oyuncular: maç günü resmi açıklamalardan; maç gününden önce şu ana kadar açıklananlar ("maç günü yeniden kontrol edilecek" notuyla). Ön bilgi kapalıysa "maç günü netleşir".
function Absences({ match, report }: { match: MatchSummary; report: AbsenceReport }) {
  const { t, loc, locale } = useI18n()
  return (
    <section className="absences">
      <h3 className="section-title">{t('absences.title')}</h3>
      {report.state === 'Preliminary' && <p className="absences-note">{t('absences.preliminary')}</p>}
      {report.state === 'Pending' ? <p className="absences-none">{t('absences.pending')}</p>
        : !hasAbsenceList(report) ? <p className="absences-none">{t('absences.unavailable')}</p>
        : report.players.length === 0 ? <p className="absences-none">{t('absences.none')}</p>
        : (
          <div className="absences-grid">
            {groupAbsences(match, report.players).map(g => (
              <div key={g.team} className="absences-team">
                <h4>{g.team}</h4>
                {g.absences.length === 0 ? <p className="absences-none">{t('absences.teamNone')}</p> : (
                  <ul>
                    {g.absences.map(a => (
                      <li key={a.player}>
                        <span>{a.player}</span>
                        <span className={`absence-tag absence-${a.kind}`}>{t(ABSENCE_LABEL[a.kind])}</span>
                        {loc(a.reason) && <span className="absence-reason">{loc(a.reason)}</span>}
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            ))}
          </div>
        )}
      {hasAbsenceList(report) && (
        <p className="absences-meta">
          {report.checkedAt && t('absences.checkedAt', { time: new Date(report.checkedAt).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' }) })}
          {report.sources.length > 0 && (
            <>
              {report.checkedAt && ' · '}
              {t('absences.sources')}:{' '}
              {report.sources.map((src, i) => (
                <Fragment key={src.url}>
                  {i > 0 && ', '}
                  <a href={src.url} target="_blank" rel="noopener noreferrer">{sourceLabel(src)}</a>
                </Fragment>
              ))}
            </>
          )}
        </p>
      )}
    </section>
  )
}

// Maçın paylaşılabilir adresini (#match/<id>) panoya kopyalar; herkese görünür. Açan kişinin yetkisini API denetler.
function ShareLink({ id }: { id: number }) {
  const { t } = useI18n()
  const [copied, setCopied] = useState(false)
  const copy = () => {
    navigator.clipboard?.writeText(matchUrl(id)).then(() => {
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    }, () => undefined)
  }
  return (
    <button type="button" className="link-btn share-link" onClick={copy} title={t('share.title')}>
      {t(copied ? 'share.copied' : 'share.copy')}
    </button>
  )
}

export default function ForecastPanel({ match, forecast, loading, error, onRetry, canRetry = true }: Props) {
  const { t, pct, loc } = useI18n()

  // Bağlantıyla açılan maç: bilgisi tahminle gelir; o ana kadar yükleniyor/hata mesajı gösterilir.
  if (!match && (loading || error)) {
    return (
      <div className="empty">
        {loading && <p className="note pulse">{t('forecast.loading')}</p>}
        {error && <p className="note note-error">{error}</p>}
      </div>
    )
  }

  if (!match) {
    return (
      <div className="empty">
        <p className="empty-title">{t('forecast.emptyTitle')}</p>
        <p>{t('forecast.emptyText')}</p>
      </div>
    )
  }

  const summary = forecast ? loc(forecast.summary) : ''

  return (
    <article className="forecast">
      <header className="fixture-head">
        {/* Başlıkta yalnızca lig adı; bağlantı kopyalama alttaki işlemlerde, fikstür ID'si (Admin) maç listesinde. */}
        <p className="fixture-league">{match.league}</p>
        <h2 className="fixture-title">
          <span>{match.homeTeam}</span>
          <span className="vs">–</span>
          <span>{match.awayTeam}</span>
        </h2>
      </header>

      {loading && <p className="note pulse">{t('forecast.loading')}</p>}

      {error && (
        <div className="note note-error">
          <p>{error}</p>
          {canRetry && <button type="button" className="link-btn" onClick={onRetry}>{t('forecast.retry')}</button>}
        </div>
      )}

      {forecast && (
        <>
          {summary && <p className="summary">{summary}</p>}

          {forecast.absences?.referee && <Referee referee={forecast.absences.referee} />}
          {forecast.absences && <Absences match={forecast.match} report={forecast.absences} />}

          <Outcomes o={forecast.outcomes} />

          <h3 className="section-title">{t('forecast.top', { n: forecast.predictions.length })}</h3>
          <ol className="picks">
            {forecast.predictions.map(p => (
              <li key={`${p.market}-${p.selection}`} className="pick">
                <span className="pick-rank">{p.rank}</span>
                <div className="pick-body">
                  <div className="pick-line">
                    <h3 className="pick-label">{loc(p.label)}</h3>
                  </div>
                  <p className="pick-market">{p.market}: {p.selection}</p>

                  <div className="meter" role="img" aria-label={t('forecast.probabilityAria', { p: pct(p.confidence) })}>
                    <span className="meter-fill" style={{ width: `${p.confidence}%` }} />
                  </div>
                  <p className="meter-legend">
                    <span>{t('forecast.probability', { p: pct(p.confidence) })}</span>
                  </p>

                  {loc(p.reasoning) && <p className="pick-reason">{loc(p.reasoning)}</p>}
                </div>
              </li>
            ))}
          </ol>

          <StatsTable stats={forecast.stats} />

          <footer className="forecast-foot">
            <span>{t(absenceFootnote(forecast.absences))}</span>
            <span className="forecast-actions">
              <ShareLink id={forecast.match.id} />
              {canRetry && <button type="button" className="link-btn" onClick={onRetry}>{t('forecast.reanalyze')}</button>}
            </span>
          </footer>
        </>
      )}
    </article>
  )
}