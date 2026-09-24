import { useEffect, useMemo, useRef, useState } from 'react'
import { getForecast, getMatches } from './api'
import type { ForecastResult, MatchSummary } from './types'
import MatchList from './components/MatchList'
import ForecastPanel from './components/ForecastPanel'

const toIsoDate = (d: Date) => {
  const local = new Date(d.getTime() - d.getTimezoneOffset() * 60000)
  return local.toISOString().slice(0, 10)
}

const shiftDate = (iso: string, days: number) => {
  const d = new Date(`${iso}T12:00:00`)
  d.setDate(d.getDate() + days)
  return toIsoDate(d)
}

export default function App() {
  const [date, setDate] = useState(() => toIsoDate(new Date()))
  const [query, setQuery] = useState('')
  const [matches, setMatches] = useState<MatchSummary[]>([])
  const [matchesState, setMatchesState] = useState<{ loading: boolean; error?: string }>({ loading: true })

  const [selectedId, setSelectedId] = useState<number | null>(null)
  const [forecast, setForecast] = useState<ForecastResult | null>(null)
  const [forecastState, setForecastState] = useState<{ loading: boolean; error?: string }>({ loading: false })

  useEffect(() => {
    let cancelled = false
    setMatchesState({ loading: true })
    getMatches(date)
      .then(data => { if (!cancelled) { setMatches(data); setMatchesState({ loading: false }) } })
      .catch((e: Error) => { if (!cancelled) setMatchesState({ loading: false, error: e.message }) })
    return () => { cancelled = true }
  }, [date])

  const filtered = useMemo(() => {
    const q = query.trim().toLocaleLowerCase('tr')
    if (!q) return matches
    return matches.filter(m =>
      [m.homeTeam, m.awayTeam, m.league, m.country].some(s => s.toLocaleLowerCase('tr').includes(q)))
  }, [matches, query])

  // Hızlı art arda tıklamalarda eski cevabın yenisini ezmemesi için
  const requestSeq = useRef(0)

  const analyze = async (id: number, refresh = false) => {
    const seq = ++requestSeq.current
    setSelectedId(id)
    setForecast(null)
    setForecastState({ loading: true })
    try {
      const result = await getForecast(id, refresh)
      if (seq !== requestSeq.current) return
      setForecast(result)
      setForecastState({ loading: false })
    } catch (e) {
      if (seq !== requestSeq.current) return
      setForecastState({ loading: false, error: (e as Error).message })
    }
  }

  const selected = matches.find(m => m.id === selectedId) ?? null

  return (
    <div className="app">
      <header className="topbar">
        <h1 className="brand">MatchForecast</h1>
        <div className="date-nav">
          <button type="button" onClick={() => setDate(d => shiftDate(d, -1))} aria-label="Önceki gün">‹</button>
          <input type="date" value={date} onChange={e => e.target.value && setDate(e.target.value)} aria-label="Tarih" />
          <button type="button" onClick={() => setDate(d => shiftDate(d, 1))} aria-label="Sonraki gün">›</button>
        </div>
      </header>

      <main className="layout">
        <section className="fixtures" aria-label="Maçlar">
          <input
            className="search"
            type="search"
            placeholder="Takım veya lig ara"
            value={query}
            onChange={e => setQuery(e.target.value)}
          />
          <MatchList
            matches={filtered}
            loading={matchesState.loading}
            error={matchesState.error}
            selectedId={selectedId}
            onSelect={id => analyze(id)}
          />
        </section>

        <section className="analysis" aria-live="polite">
          <ForecastPanel
            match={selected}
            forecast={forecast}
            loading={forecastState.loading}
            error={forecastState.error}
            onRetry={() => selectedId && analyze(selectedId, true)}
          />
        </section>
      </main>
    </div>
  )
}
