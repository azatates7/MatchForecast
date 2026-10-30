import type { ForecastResult, MatchSummary, OutcomeProbabilities } from '../types'
import StatsTable from './StatsTable'

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
  const parts = [
    { key: '1', name: 'Ev', value: home, className: 'seg-home' },
    { key: 'X', name: 'Beraberlik', value: draw, className: 'seg-draw' },
    { key: '2', name: 'Deplasman', value: away, className: 'seg-away' },
  ]
  return (
    <div className="result">
      <p className="result-title">{title}</p>
      <div className="result-bar" role="img" aria-label={`${title}: 1 %${home}, X %${draw}, 2 %${away}`}>
        {parts.map(p => (
          <span key={p.key} className={`result-seg ${p.className}`} style={{ flexGrow: p.value }}>
            {p.value >= 12 && `${p.key} %${p.value}`}
          </span>
        ))}
      </div>
      <p className="result-legend" aria-hidden="true">
        {parts.map(p => (
          <span key={p.key} className="legend-item">
            <i className={`legend-dot ${p.className}`} />
            {p.name} <strong>%{p.value}</strong>
          </span>
        ))}
      </p>
    </div>
  )
}

function Outcomes({ o }: { o: OutcomeProbabilities }) {
  return (
    <section className="outcomes">
      <ResultBar title="Maç sonucu" home={o.home} draw={o.draw} away={o.away} />
      {o.firstHalfHome != null && o.firstHalfDraw != null && o.firstHalfAway != null && (
        <ResultBar title="İlk yarı" home={o.firstHalfHome} draw={o.firstHalfDraw} away={o.firstHalfAway} />
      )}
      <p className="btts">Karşılıklı gol: <strong>Var %{o.bttsYes}</strong> · Yok %{100 - o.bttsYes}</p>
    </section>
  )
}

export default function ForecastPanel({ match, forecast, loading, error, onRetry, canRetry = true }: Props) {
  if (!match) {
    return (
      <div className="empty">
        <p className="empty-title">Bir maç seçin</p>
        <p>Yapay zeka seçtiğiniz maçın gol, korner ve kart beklentilerini çıkarır; bunlardan hesaplanan, olasılığı en yüksek 10 tahmin listelenir.</p>
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

      {loading && <p className="note pulse">Maç analiz ediliyor, bu 10-30 saniye sürebilir…</p>}

      {error && (
        <div className="note note-error">
          <p>{error}</p>
          {canRetry && <button type="button" className="link-btn" onClick={onRetry}>Tekrar analiz et</button>}
        </div>
      )}

      {forecast && (
        <>
          {forecast.summary && <p className="summary">{forecast.summary}</p>}

          <Outcomes o={forecast.outcomes} />

          <h3 className="section-title">En olası 10 tahmin</h3>
          <ol className="picks">
            {forecast.predictions.map(p => (
              <li key={`${p.market}-${p.selection}`} className="pick">
                <span className="pick-rank">{p.rank}</span>
                <div className="pick-body">
                  <div className="pick-line">
                    <h3 className="pick-label">{p.label}</h3>
                  </div>
                  <p className="pick-market">{p.market}: {p.selection}</p>

                  <div className="meter" role="img" aria-label={`Gerçekleşme olasılığı %${p.confidence}`}>
                    <span className="meter-fill" style={{ width: `${p.confidence}%` }} />
                  </div>
                  <p className="meter-legend">
                    <span>Olasılık %{p.confidence}</span>
                  </p>

                  {p.reasoning && <p className="pick-reason">{p.reasoning}</p>}
                </div>
              </li>
            ))}
          </ol>

          <StatsTable stats={forecast.stats} />

          <footer className="forecast-foot">
            <span>Değerler yapay zekanın genel bilgisine dayanır; güncel kadro, sakatlık ve hakem bilgisi içermez.</span>
            {canRetry && <button type="button" className="link-btn" onClick={onRetry}>Yeniden analiz et</button>}
          </footer>
          <p className="disclaimer">Tahminler olasılık tahminidir, sonuç garantisi vermez.</p>
        </>
      )}
    </article>
  )
}