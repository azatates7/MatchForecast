import type { ForecastResult, MatchSummary } from '../types'

interface Props {
  match: MatchSummary | null
  forecast: ForecastResult | null
  loading: boolean
  error?: string
  onRetry: () => void
}

export default function ForecastPanel({ match, forecast, loading, error, onRetry }: Props) {
  if (!match) {
    return (
      <div className="empty">
        <p className="empty-title">Bir maç seçin</p>
        <p>Seçtiğiniz maçın tüm bahis marketleri yapay zekaya gönderilir ve gerçekleşme ihtimali en yüksek 5 seçenek listelenir.</p>
      </div>
    )
  }

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

      {loading && <p className="note pulse">Oranlar analiz ediliyor, bu 10-30 saniye sürebilir…</p>}

      {error && (
        <div className="note note-error">
          <p>{error}</p>
          <button type="button" className="link-btn" onClick={onRetry}>Tekrar analiz et</button>
        </div>
      )}

      {forecast && (
        <>
          {forecast.summary && <p className="summary">{forecast.summary}</p>}

          <ol className="picks">
            {forecast.predictions.map(p => (
              <li key={`${p.market}-${p.selection}`} className="pick">
                <span className="pick-rank">{p.rank}</span>
                <div className="pick-body">
                  <div className="pick-line">
                    <h3 className="pick-label">{p.label}</h3>
                    <span className="pick-odd" title="Oran">{p.odd.toFixed(2)}</span>
                  </div>
                  <p className="pick-market">{p.market}: {p.selection}</p>

                  <div
                    className="meter"
                    role="img"
                    aria-label={`Yapay zeka güveni %${p.confidence}, oranın ima ettiği olasılık %${p.impliedProbability}`}
                  >
                    <span className="meter-fill" style={{ width: `${p.confidence}%` }} />
                    <span className="meter-market" style={{ left: `${Math.min(p.impliedProbability, 100)}%` }} />
                  </div>
                  <p className="meter-legend">
                    <span>Yapay zeka %{p.confidence}</span>
                    <span>Oran ima ediyor %{p.impliedProbability}</span>
                  </p>

                  {p.reasoning && <p className="pick-reason">{p.reasoning}</p>}
                </div>
              </li>
            ))}
          </ol>

          <footer className="forecast-foot">
            <span>Oranlar: {forecast.bookmaker}</span>
            <button type="button" className="link-btn" onClick={onRetry}>Yeniden analiz et</button>
          </footer>
          <p className="disclaimer">Tahminler olasılık tahminidir, sonuç garantisi vermez.</p>
        </>
      )}
    </article>
  )
}
