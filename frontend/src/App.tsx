import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { clearCache, getForecast, getMatches, getMe, hasSession, logout, onSessionExpired } from './api'
import type { MeResponse } from './types'
import type { ForecastResult, MatchSummary } from './components/Types'
import { isLive } from './components/MatchStatus'
import MatchList from './components/MatchList'
import ForecastPanel from './components/ForecastPanel'
import AuthPanel from './components/Auth-Panel'
import LeagueFilter from './components/LeagueFilter'

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

// "Bugün" tarayıcının saat diliminden bağımsız olarak İstanbul'a göre (API ve fikstürler bu saat diliminde) YYYY-MM-DD
const todayInIstanbul = () => {
  const parts = new Intl.DateTimeFormat('en-US', { timeZone: 'Europe/Istanbul', year: 'numeric', month: '2-digit', day: '2-digit' })
    .formatToParts(new Date())
  const get = (type: string) => parts.find(p => p.type === type)?.value ?? ''
  return `${get('year')}-${get('month')}-${get('day')}`
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

  const [date, setDate] = useState(todayInIstanbul)
  const [query, setQuery] = useState('')
  // Seçili lig anahtarları ("Ülke / Lig"); boş dizi = "Tümü", yani lig filtresi yok.
  const [leagues, setLeagues] = useState<string[]>([])
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
    if (isGuest) setDate(todayInIstanbul())
  }, [isGuest])

  // Tarih değişince (veya giriş yapılınca) listeyi yükle ve lig filtresini sıfırla (yeni tarihte o lig olmayabilir)
  useEffect(() => {
    if (!loggedIn) return
    let cancelled = false
    setLeagues([])
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

  // Arama metnine uyan maçlar. Lig filtresindeki sayılar da bunlardan hesaplanır ki aramayla tutarlı olsun.
  const searched = useMemo(() => {
    const q = query.trim().toLocaleLowerCase('tr')
    if (!q) return matches
    return matches.filter(m => [m.homeTeam, m.awayTeam, m.league, m.country].some(s => s.toLocaleLowerCase('tr').includes(q)))
  }, [matches, query])

  // Lig seçenekleri: aramaya uyan maçlardan, maç sayısıyla birlikte, alfabetik.
  // Seçili ligler aramada hiç maç içermese de listede (0) olarak kalır; yoksa seçim görünmez olurdu.
  const leagueOptions = useMemo(() => {
    const counts = new Map<string, number>()
    for (const m of searched) counts.set(leagueKey(m), (counts.get(leagueKey(m)) ?? 0) + 1)
    for (const l of leagues) if (!counts.has(l)) counts.set(l, 0)
    return [...counts].sort(([a], [b]) => a.localeCompare(b, 'tr'))
  }, [searched, leagues])

  const filtered = useMemo(
    () => (leagues.length > 0 ? searched.filter(m => leagues.includes(leagueKey(m))) : searched),
    [searched, leagues])

  // Bir lig seçilince/kaldırılınca. Hiç lig kalmazsa veya listedeki tüm ligler seçilirse "Tümü"ye dönülür (filtre kalkar).
  const toggleLeague = (key: string) =>
    setLeagues(prev => {
      const next = prev.includes(key) ? prev.filter(l => l !== key) : [...prev, key]
      return leagueOptions.every(([k]) => next.includes(k)) ? [] : next
    })

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

  // Misafirin bugün tek maçı varsa beklemeden o maçın tahmini açılır (listeden seçim gerekmez).
  // Yalnızca hiç maç seçilmemişken çalışır; arama sonucu tek maça düşünce tetiklenmesin diye tüm liste (matches) kullanılır.
  useEffect(() => {
    if (!isGuest || matchesState.loading || matchesState.error || selectedId !== null || matches.length !== 1) return
    analyze(matches[0].id)
    // analyze her render'da yeniden oluşur; bağımlılığa eklenirse etki gereksiz yere tekrar çalışır.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isGuest, matchesState, selectedId, matches])

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
          {isGuest && (
            <div className="guest-notice" role="note">
              <svg className="guest-notice-icon" viewBox="0 0 24 24" width="16" height="16" aria-hidden="true"><path fill="currentColor" d="M12 2a5 5 0 0 0-5 5v3H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-8a2 2 0 0 0-2-2h-1V7a5 5 0 0 0-5-5Zm-3 8V7a3 3 0 0 1 6 0v3H9Zm3 4a2 2 0 0 1 1 3.73V19h-2v-1.27A2 2 0 0 1 12 14Z"/></svg>
              <span>Misafir kullanıcı olarak yalnızca bugünün Türkiye Süper Lig maçlarını görüyorsun.</span>
              <button type="button" className="link-btn" onClick={handleLogout}>Tüm ligleri görmek için giriş yap</button>
            </div>
          )}
          <input
            className="search"
            type="search"
            placeholder="Takım veya lig ara"
            value={query}
            onChange={e => setQuery(e.target.value)}
          />
          {/* Misafir: API yalnızca Süper Lig döndürdüğü için lig seçimi gösterilmez. */}
          {!isGuest && (
            <LeagueFilter
              options={leagueOptions}
              selected={leagues}
              total={searched.length}
              selectedCount={filtered.length}
              onSelectAll={() => setLeagues([])}
              onToggle={toggleLeague}
            />
          )}
          <MatchList
            matches={filtered}
            loading={matchesState.loading}
            error={matchesState.error}
            selectedId={selectedId}
            onSelect={id => analyze(id)}
            fetchedAt={fetchedAt}
            now={now}
            emptyText={isGuest ? 'Bugün Süper Lig maçı yok.' : undefined}
          />
        </section>

        <section className="analysis" aria-live="polite">
          <ForecastPanel
            match={selected}
            forecast={forecast}
            loading={forecastState.loading}
            error={forecastState.error}
            onRetry={() => selectedId && analyze(selectedId, true)}
            canRetry={!isGuest}
          />
        </section>
      </main>
    </div>
  )
}