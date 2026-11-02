import type { ForecastResult, MatchSummary, OutcomeProbabilities } from '../types'
import StatsTable from './StatsTable'
import { useI18n } from '../i18n/useI18n'

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
function ResultBar({ title, home, draw, away }: { title: string; home: number; draw: number; away: number }) {
  const { t, pct } = useI18n()
  const parts = [
    { key: '1', name: t('outcome.home'), value: home, className: 'seg-home' },
    { key: 'X', name: t('outcome.draw'), value: draw, className: 'seg-draw' },
    { key: '2', name: t('outcome.away'), value: away, className: 'seg-away' },
  ]
  return (
    <div className="result">
      <p className="result-title">{title}</p>
      <div className="result-bar" role="img" aria-label={`${title}: 1 ${pct(home)}, X ${pct(draw)}, 2 ${pct(away)}`}>
        {parts.map(p => (
          <span key={p.key} className={`result-seg ${p.className}`} style={{ flexGrow: p.value }}>
            {p.value >= 12 && `${p.key} ${pct(p.value)}`}
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
      <ResultBar title={t('outcome.fullTime')} home={o.home} draw={o.draw} away={o.away} />
      {o.firstHalfHome != null && o.firstHalfDraw != null && o.firstHalfAway != null && (
        <ResultBar title={t('outcome.firstHalf')} home={o.firstHalfHome} draw={o.firstHalfDraw} away={o.firstHalfAway} />
      )}
      <p className="btts">
        {t('outcome.btts')} <strong>{t('outcome.yes')} {pct(o.bttsYes)}</strong> · {t('outcome.no')} {pct(100 - o.bttsYes)}
      </p>
    </section>
  )
}

export default function ForecastPanel({ match, forecast, loading, error, onRetry, canRetry = true }: Props) {
  const { t, pct, loc } = useI18n()

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
            <span>{t('forecast.footnote')}</span>
            {canRetry && <button type="button" className="link-btn" onClick={onRetry}>{t('forecast.reanalyze')}</button>}
          </footer>
          <p className="disclaimer">{t('forecast.disclaimer')}</p>
        </>
      )}
    </article>
  )
}