import { Component, computed, effect, signal } from '@angular/core';
import { clearCache, getForecast, getMatches, getMe, hasSession, logout, onSessionExpired } from './core/api';
import type { ForecastResult, MatchSummary, MeResponse } from './core/types';
import { isLive } from './core/match-status';
import { MatchList } from './components/match-list';
import { ForecastPanel } from './components/forecast-panel';
import { AuthPanel } from './components/auth-panel';
import { LeagueFilter } from './components/league-filter';

// Canlı maç varken listenin arka planda yenilenme aralığı. API kotasını korumak için
// kısa tutulmadı; aradaki dakikalar minuteLabel içinde yerelde ilerletilir.
const LIVE_REFRESH_MS = 5 * 60_000;
// Ekrandaki dakika etiketinin güncellenme aralığı (ağ isteği yapmaz)
const CLOCK_TICK_MS = 30_000;
// Cache temizleme sonucunun ekranda kalma süresi
const CACHE_MSG_MS = 4_000;

const toIsoDate = (d: Date) => {
  const local = new Date(d.getTime() - d.getTimezoneOffset() * 60000);
  return local.toISOString().slice(0, 10);
};

// "Bugün" tarayıcının saat diliminden bağımsız olarak İstanbul'a göre (API ve fikstürler bu saat diliminde) YYYY-MM-DD
const todayInIstanbul = () => {
  const parts = new Intl.DateTimeFormat('en-US', { timeZone: 'Europe/Istanbul', year: 'numeric', month: '2-digit', day: '2-digit' })
    .formatToParts(new Date());
  const get = (type: string) => parts.find(p => p.type === type)?.value ?? '';
  return `${get('year')}-${get('month')}-${get('day')}`;
};

const shiftDate = (iso: string, days: number) => {
  const d = new Date(`${iso}T12:00:00`);
  d.setDate(d.getDate() + days);
  return toIsoDate(d);
};

const leagueKey = (m: MatchSummary) => `${m.country} / ${m.league}`;

interface LoadState { loading: boolean; error?: string }
interface CacheState { clearing: boolean; message?: string; error?: boolean }

@Component({
  selector: 'app-root',
  imports: [MatchList, ForecastPanel, AuthPanel, LeagueFilter],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  // Kayıtlı refresh token varsa giriş yapılmış sayılır; geçersizse ilk istekte login ekranına dönülür.
  protected readonly loggedIn = signal(hasSession());

  protected readonly date = signal(todayInIstanbul());
  protected readonly query = signal('');
  // Seçili lig anahtarları ("Ülke / Lig"); boş dizi = "Tümü", yani lig filtresi yok.
  protected readonly leagues = signal<string[]>([]);
  protected readonly matches = signal<MatchSummary[]>([]);
  protected readonly fetchedAt = signal(Date.now());
  protected readonly now = signal(Date.now());
  protected readonly matchesState = signal<LoadState>({ loading: true });

  protected readonly selectedId = signal<number | null>(null);
  protected readonly forecast = signal<ForecastResult | null>(null);
  protected readonly forecastState = signal<LoadState>({ loading: false });
  protected readonly cacheState = signal<CacheState>({ clearing: false });
  protected readonly me = signal<MeResponse | null>(null);

  // Misafir: yalnızca bugünün Süper Lig maçları, tarih seçimi ve cache temizleme yok (API de aynı kuralları uygular).
  protected readonly isGuest = computed(() => this.me()?.roles.includes('Guest') ?? false);

  protected readonly hasLive = computed(() => this.matches().some(isLive));

  // Arama metnine uyan maçlar. Lig filtresindeki sayılar da bunlardan hesaplanır ki aramayla tutarlı olsun.
  protected readonly searched = computed(() => {
    const q = this.query().trim().toLocaleLowerCase('tr');
    if (!q) return this.matches();
    return this.matches().filter(m => [m.homeTeam, m.awayTeam, m.league, m.country].some(s => s.toLocaleLowerCase('tr').includes(q)));
  });

  // Lig seçenekleri: aramaya uyan maçlardan, maç sayısıyla birlikte, alfabetik.
  // Seçili ligler aramada hiç maç içermese de listede (0) olarak kalır; yoksa seçim görünmez olurdu.
  protected readonly leagueOptions = computed(() => {
    const counts = new Map<string, number>();
    for (const m of this.searched()) counts.set(leagueKey(m), (counts.get(leagueKey(m)) ?? 0) + 1);
    for (const l of this.leagues()) if (!counts.has(l)) counts.set(l, 0);
    return [...counts].sort(([a], [b]) => a.localeCompare(b, 'tr'));
  });

  protected readonly filtered = computed(() => {
    const leagues = this.leagues();
    return leagues.length > 0 ? this.searched().filter(m => leagues.includes(leagueKey(m))) : this.searched();
  });

  protected readonly selected = computed(() => this.matches().find(m => m.id === this.selectedId()) ?? null);

  // Hızlı art arda tıklamalarda eski cevabın yenisini ezmemesi için
  private requestSeq = 0;

  constructor() {
    // Refresh token da geçersizse (süresi doldu, iptal edildi) api.ts haber verir: login ekranına dön.
    onSessionExpired(() => this.loggedIn.set(false));

    // Giriş yapılınca üst barda gösterilecek ad ve roller alınır
    effect(onCleanup => {
      if (!this.loggedIn()) return;
      let cancelled = false;
      getMe().then(data => { if (!cancelled) this.me.set(data); }).catch(() => undefined);
      onCleanup(() => { cancelled = true; });
    });

    // Misafir her zaman bugünü görür (önceki oturumda başka bir tarih seçilmiş olabilir)
    effect(() => {
      if (this.isGuest()) this.date.set(todayInIstanbul());
    });

    // Tarih değişince (veya giriş yapılınca) listeyi yükle ve lig filtresini sıfırla (yeni tarihte o lig olmayabilir)
    effect(onCleanup => {
      if (!this.loggedIn()) return;
      const date = this.date();
      let cancelled = false;
      this.leagues.set([]);
      this.loadMatches(date, false, () => cancelled);
      onCleanup(() => { cancelled = true; });
    });

    // Canlı maç varsa: listeyi periyodik yenile ve dakika etiketini ilerlet
    effect(onCleanup => {
      if (!this.loggedIn() || !this.hasLive()) return;
      let cancelled = false;
      const refresh = setInterval(() => this.loadMatches(this.date(), true, () => cancelled), LIVE_REFRESH_MS);
      const tick = setInterval(() => this.now.set(Date.now()), CLOCK_TICK_MS);
      onCleanup(() => { cancelled = true; clearInterval(refresh); clearInterval(tick); });
    });

    // Bilgi mesajı birkaç saniye sonra kaybolur
    effect(onCleanup => {
      if (!this.cacheState().message) return;
      const timer = setTimeout(() => this.cacheState.update(s => ({ clearing: s.clearing })), CACHE_MSG_MS);
      onCleanup(() => clearTimeout(timer));
    });
  }

  // Giriş, kayıt veya Google ile giriş başarılı olunca AuthPanel haber verir.
  protected onAuthenticated() {
    this.loggedIn.set(true);
  }

  // Ekrandaki veriler temizlenir; başka bir kullanıcı giriş yaparsa önceki oturumun verisini görmesin.
  protected async handleLogout() {
    await logout();
    this.loggedIn.set(false);
    this.me.set(null);
    this.matches.set([]);
    this.selectedId.set(null);
    this.forecast.set(null);
  }

  // silent=true: arka plan yenilemesi; listeyi "yükleniyor" durumuna sokmaz, hata olursa eski listeyi korur.
  private loadMatches(date: string, silent: boolean, isCancelled: () => boolean = () => false) {
    if (!silent) this.matchesState.set({ loading: true });
    getMatches(date)
      .then(data => {
        if (isCancelled()) return;
        this.matches.set(data);
        this.fetchedAt.set(Date.now());
        this.now.set(Date.now());
        this.matchesState.set({ loading: false });
      })
      .catch((e: Error) => {
        if (isCancelled() || silent) return;
        this.matchesState.set({ loading: false, error: e.message });
      });
  }

  protected shiftDay(days: number) {
    this.date.update(d => shiftDate(d, days));
  }

  protected onDateInput(value: string) {
    if (value) this.date.set(value);
  }

  // Bir lig seçilince/kaldırılınca. Hiç lig kalmazsa veya listedeki tüm ligler seçilirse "Tümü"ye dönülür (filtre kalkar).
  protected toggleLeague(key: string) {
    this.leagues.update(prev => {
      const next = prev.includes(key) ? prev.filter(l => l !== key) : [...prev, key];
      return this.leagueOptions().every(([k]) => next.includes(k)) ? [] : next;
    });
  }

  protected async analyze(id: number, refresh = false) {
    const seq = ++this.requestSeq;
    this.selectedId.set(id);
    this.forecast.set(null);
    this.forecastState.set({ loading: true });
    try {
      const result = await getForecast(id, refresh);
      if (seq !== this.requestSeq) return;
      this.forecast.set(result);
      this.forecastState.set({ loading: false });
    } catch (e) {
      if (seq !== this.requestSeq) return;
      this.forecastState.set({ loading: false, error: (e as Error).message });
    }
  }

  protected retry() {
    const id = this.selectedId();
    if (id) this.analyze(id, true);
  }

  // Cache silindikten sonra maç listesi API'den taze çekilir; ekrandaki tahmin korunur, sonraki analiz AI'dan yeniden üretilir.
  protected async handleClearCache() {
    this.cacheState.set({ clearing: true });
    try {
      const { deletedKeys } = await clearCache();
      this.cacheState.set({ clearing: false, message: `Önbellek temizlendi (${deletedKeys} kayıt).` });
      this.loadMatches(this.date(), false);
    } catch (e) {
      this.cacheState.set({ clearing: false, message: (e as Error).message, error: true });
    }
  }
}