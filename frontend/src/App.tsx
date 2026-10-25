import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { clearCache, getForecast, getMatches, getMe, hasSession, logout, onSessionExpired } from './api'
import type { MeResponse } from './types'
import type { ForecastResult, MatchSummary } from './components/Types'
import { isLive } from './components/MatchStatus'
import MatchList from './components/MatchList'
import ForecastPanel from './components/ForecastPanel'
import AuthPanel from './components/Auth-Panel'

// Canlı maç varken listenin arka planda yenilenme aralığı. API kotasını korumak için
// kısa tutulmadı; aradaki dakikalar matchStatus.minuteLabel içinde yerelde ilerletilir.
const LIVE_REFRESH_MS = 5 * 60_000
// Ekrandaki dakika etiketinin güncellenme aralığı (ağ isteği yapmaz)
const CLOCK_TICK_MS = 30_000
// Cache temizleme sonucunun ekranda kalma süresi
const CACHE_MSG_MS = 4_000

const toIsoDate = (d: Date) => {
  const local = new Date(d.getTime() - d.getTimezoneOffset() * 60000)
  return local.toISOString().slice(0, 10)
}

const shiftDate = (iso: string, days: number) => {
  const d = new Date(`${iso}T12:00:00`)
  d.setDate(d.getDate() + days)
  return toIsoDate(d)
}

const leagueKey = (m: MatchSummary) => `${m.country} / ${m.league}`

export default function App() {
  // Kayıtlı refresh token varsa giriş yapılmış sayılır; geçersizse ilk istekte login ekranına dönülür.
  const [loggedIn, setLoggedIn] = useState(hasSession)

  const [date, setDate] = useState(() => toIsoDate(new Date()))
  const [query, setQuery] = useState('')
  const [league, setLeague] = useState('')
  const [matches, setMatches] = useState<MatchSummary[]>([])
  const [fetchedAt, setFetchedAt] = useState(() => Date.now())
  const [now, setNow] = useState(() => Date.now())
  const [matchesState, setMatchesState] = useState<{ loading: boolean; error?: string }>({ loading: true })

  const [selectedId, setSelectedId] = useState<number | null>(null)
  const [forecast, setForecast] = useState<ForecastResult | null>(null)
  const [forecastState, setForecastState] = useState<{ loading: boolean; error?: string }>({ loading: false })
  const [cacheState, setCacheState] = useState<{ clearing: boolean; message?: string; error?: boolean }>({ clearing: false })
  const [me, setMe] = useState<MeResponse | null>(null)

  // Misafir: yalnızca bugünün Süper Lig maçları, tarih seçimi ve cache temizleme yok (API de aynı kuralları uygular).
  const isGuest = me?.roles.includes('Guest') ?? false

  // silent=true: arka plan yenilemesi; listeyi "yükleniyor" durumuna sokmaz, hata olursa eski listeyi korur.
  const loadMatches = useCallback((silent: boolean, isCancelled: () => boolean = () => false) => {
    if (!silent) setMatchesState({ loading: true })
    getMatches(date)
      .then(data => {
        if (isCancelled()) return
        setMatches(data)
        setFetchedAt(Date.now())
        setNow(Date.now())
        setMatchesState({ loading: false })
      })
      .catch((e: Error) => {
        if (isCancelled() || silent) return
        setMatchesState({ loading: false, error: e.message })
      })
  }, [date])

  // Refresh token da geçersizse (süresi doldu, iptal edildi) api.ts haber verir: login ekranına dön.
  useEffect(() => {
    onSessionExpired(() => setLoggedIn(false))
  }, [])

  // Giriş yapılınca üst barda gösterilecek ad ve roller alınır
  useEffect(() => {
    if (!loggedIn) return
    let cancelled = false
    getMe().then(data => { if (!cancelled) setMe(data) }).catch(() => undefined)
    return () => { cancelled = true }
  }, [loggedIn])

  // Misafir her zaman bugünü görür (önceki oturumda başka bir tarih seçilmiş olabilir)
  useEffect(() => {
    if (isGuest) setDate(toIsoDate(new Date()))
  }, [isGuest])

  // Tarih değişince (veya giriş yapılınca) listeyi yükle ve lig filtresini sıfırla (yeni tarihte o lig olmayabilir)
  useEffect(() => {
    if (!loggedIn) return
    let cancelled = false
    setLeague('')
    loadMatches(false, () => cancelled)
    return () => { cancelled = true }
  }, [loadMatches, loggedIn])

  const hasLive = useMemo(() => matches.some(isLive), [matches])

  // Canlı maç varsa: listeyi periyodik yenile ve dakika etiketini ilerlet
  useEffect(() => {
    if (!loggedIn || !hasLive) return
    let cancelled = false
    const refresh = setInterval(() => loadMatches(true, () => cancelled), LIVE_REFRESH_MS)
    const tick = setInterval(() => setNow(Date.now()), CLOCK_TICK_MS)
    return () => { cancelled = true; clearInterval(refresh); clearInterval(tick) }
  }, [hasLive, loadMatches, loggedIn])

  // Lig seçenekleri: yüklenen maçlardan, maç sayısıyla birlikte, alfabetik
  const leagueOptions = useMemo(() => {
    const counts = new Map<string, number>()
    for (const m of matches) counts.set(leagueKey(m), (counts.get(leagueKey(m)) ?? 0) + 1)
    return [...counts].sort(([a], [b]) => a.localeCompare(b, 'tr'))
  }, [matches])

  const filtered = useMemo(() => {
    const q = query.trim().toLocaleLowerCase('tr')
    return matches.filter(m =>
      (!league || leagueKey(m) === league) &&
      (!q || [m.homeTeam, m.awayTeam, m.league, m.country].some(s => s.toLocaleLowerCase('tr').includes(q))))
  }, [matches, query, league])

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

  // Cache silindikten sonra maç listesi API'den taze çekilir; ekrandaki tahmin korunur, sonraki analiz AI'dan yeniden üretilir.
  const handleClearCache = async () => {
    setCacheState({ clearing: true })
    try {
      const { deletedKeys } = await clearCache()
      setCacheState({ clearing: false, message: `Önbellek temizlendi (${deletedKeys} kayıt).` })
      loadMatches(false)
    } catch (e) {
      setCacheState({ clearing: false, message: (e as Error).message, error: true })
    }
  }

  // Bilgi mesajı birkaç saniye sonra kaybolur
  useEffect(() => {
    if (!cacheState.message) return
    const timer = setTimeout(() => setCacheState(s => ({ clearing: s.clearing })), CACHE_MSG_MS)
    return () => clearTimeout(timer)
  }, [cacheState.message])

  // Ekrandaki veriler temizlenir; başka bir kullanıcı giriş yaparsa önceki oturumun verisini görmesin.
  const handleLogout = async () => {
    await logout()
    setLoggedIn(false)
    setMe(null)
    setMatches([])
    setSelectedId(null)
    setForecast(null)
  }

  const selected = matches.find(m => m.id === selectedId) ?? null

  // Giriş, kayıt veya Google ile giriş başarılı olunca AuthPanel haber verir.
  if (!loggedIn) return <AuthPanel onAuthenticated={() => setLoggedIn(true)} />

  return (
    <div className="app">
      <header className="topbar">
        <h1 className="brand">MatchForecast</h1>
        {isGuest ? (
          <div className="date-nav">
            <input type="date" value={date} disabled aria-label="Tarih (misafir yalnızca bugünü görür)" />
          </div>
        ) : (
          <div className="date-nav">
            <button type="button" onClick={() => setDate(d => shiftDate(d, -1))} aria-label="Önceki gün">‹</button>
            <input type="date" value={date} onChange={e => e.target.value && setDate(e.target.value)} aria-label="Tarih" />
            <button type="button" onClick={() => setDate(d => shiftDate(d, 1))} aria-label="Sonraki gün">›</button>
          </div>
        )}
        <div className="cache-actions">
          {cacheState.message && (
            <span className={cacheState.error ? 'cache-msg cache-msg-error' : 'cache-msg'} role="status">{cacheState.message}</span>
          )}
          {me && <span className="user-name">{me.displayName || me.userName}</span>}
          {!isGuest && (
            <button type="button" className="cache-btn" onClick={handleClearCache} disabled={cacheState.clearing}>
              {cacheState.clearing ? 'Temizleniyor…' : 'Cache Temizle'}
            </button>
          )}
          <button type="button" className="cache-btn" onClick={handleLogout}>Çıkış</button>
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
          <select
            className="league-filter"
            value={league}
            onChange={e => setLeague(e.target.value)}
            aria-label="Lig / turnuva filtresi"
            disabled={leagueOptions.length === 0}
          >
            <option value="">Tüm ligler ({matches.length})</option>
            {leagueOptions.map(([key, count]) => (
              <option key={key} value={key}>{key} ({count})</option>
            ))}
          </select>
          <MatchList
            matches={filtered}
            loading={matchesState.loading}
            error={matchesState.error}
            selectedId={selectedId}
            onSelect={id => analyze(id)}
            fetchedAt={fetchedAt}
            now={now}
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