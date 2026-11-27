import { Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { addFavoriteLeague, clearCache, getFavoriteLeagues, getForecast, getMatches, getMe, hasSession, logout, onSessionExpired, removeFavoriteLeague } from './core/api';
import { hasPermission, type ForecastResult, type MatchSummary, type MeResponse } from './core/types';
import { isLive } from './core/match-status';
import { MatchList } from './components/match-list';
import { ForecastPanel } from './components/forecast-panel';
import { AuthPanel } from './components/auth-panel';
import { LeagueFilter } from './components/league-filter';
import { ThemeToggle } from './components/theme-toggle';
import { LanguageToggle } from './components/language-toggle';
import { BrandLogo } from './components/brand-logo';
import { AdminPanel } from './components/admin-panel';
import { canOpenAdmin, clearHash, parseAdminHash } from './core/admin-view';
import { istanbulDate, matchHash, parseMatchHash, replaceHash } from './core/match-route';
import { I18nService } from './core/i18n/i18n.service';

// Canlı maç varken listenin arka planda yenilenme aralığı. API kotasını korumak için kısa tutulmadı; aradaki dakikalar minuteLabel içinde yerelde ilerletilir.
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
const todayInIstanbul = () => istanbulDate(new Date());

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
  imports: [MatchList, ForecastPanel, AuthPanel, LeagueFilter, ThemeToggle, LanguageToggle, AdminPanel, BrandLogo],
  templateUrl: './app.html',
  styleUrl: './app.css',
  host: { '(window:hashchange)': 'onHashChange()' },
})
export class App {
  protected readonly i18n = inject(I18nService);

  // Kayıtlı refresh token varsa giriş yapılmış sayılır; geçersizse ilk istekte login ekranına dönülür.
  protected readonly loggedIn = signal(hasSession());

  protected readonly date = signal(todayInIstanbul());
  protected readonly query = signal('');
  // Seçili lig anahtarları ("Ülke / Lig"); boş dizi = "Tümü", yani lig filtresi yok.
  protected readonly leagues = signal<string[]>([]);
  // Kullanıcının favori ligleri (API'de kullanıcıya özgü saklanır)
  protected readonly favorites = signal<string[]>([]);
  protected readonly matches = signal<MatchSummary[]>([]);
  protected readonly fetchedAt = signal(Date.now());
  protected readonly now = signal(Date.now());
  protected readonly matchesState = signal<LoadState>({ loading: true });

  protected readonly selectedId = signal<number | null>(null);
  protected readonly forecast = signal<ForecastResult | null>(null);
  protected readonly forecastState = signal<LoadState>({ loading: false });
  protected readonly cacheState = signal<CacheState>({ clearing: false });
  protected readonly me = signal<MeResponse | null>(null);
  // /api/me başarısız olursa kısıtlı görünümde takılı kalınmasın diye (API kısıtları yine uygular)
  private readonly meFailed = signal(false);

  // Misafir: yalnızca bugünün Süper Lig maçları, tarih seçimi ve cache temizleme yok (API de aynı kuralları uygular).
  protected readonly isGuest = computed(() => this.me()?.roles.includes('Guest') ?? false);
  // Rol bilgisi gelene kadar misafire kapalı alanlar (tarih okları, cache, lig filtresi) gösterilmez; misafir onları bir an bile görmesin.
  protected readonly restricted = computed(() => this.isGuest() || (this.me() === null && !this.meFailed()));
  // Önbelleği Sil yalnızca cache.clear yetkisi olana görünür (Admin dahil; API de aynı yetkiyi ister).
  protected readonly canClearCache = computed(() => hasPermission(this.me(), 'cache.clear'));
  // Fikstür ID'leri yalnızca Admin'e gösterilir (API-Football'da maçı sorgulamak, log ve Redis anahtarlarını bulmak için).
  protected readonly isAdmin = computed(() => this.me()?.roles.includes('Admin') ?? false);

  // Yönetim görünümü adresteki #admin[/sekme] ile açılır; yönetim yetkisi olmayan maç ekranında kalır.
  protected readonly hash = signal(location.hash);
  protected readonly adminRoute = computed(() => parseAdminHash(this.hash()));
  protected readonly canAdmin = computed(() => canOpenAdmin(this.me()));
  protected readonly showAdmin = computed(() => this.adminRoute().admin && this.canAdmin());

  protected onHashChange() { this.hash.set(location.hash); }

  protected openAdmin() { location.hash = 'admin'; }

  // Adresi temizler (maç seçimi de kalkar); logo ve çıkışta kullanılır.
  private resetHash() {
    clearHash();
    this.hash.set('');
  }

  // Adresteki maç (#match/<id>): paylaşılan bağlantı ve tarayıcının geri/ileri tuşu buradan çalışır.
  protected readonly routeMatchId = computed(() => parseMatchHash(this.hash()));

  // Listeden seçim adrese yazılır; seçimi adresteki değişiklik açar. Aynı maça tekrar tıklanırsa yeniden yüklenir.
  protected selectMatch(id: number) {
    if (parseMatchHash(location.hash) === id) this.analyze(id);
    else location.hash = matchHash(id);
  }

  // Yönetimden dönüşte açık maç varsa onun adresine dönülür, yoksa adres temizlenir.
  protected backToMatches() {
    const id = this.selectedId();
    if (id !== null) location.hash = matchHash(id);
    else this.resetHash();
  }

  protected readonly hasLive = computed(() => this.matches().some(isLive));

  // Arama metnine uyan maçlar. Lig filtresindeki sayılar da bunlardan hesaplanır ki aramayla tutarlı olsun.
  protected readonly searched = computed(() => {
    const q = this.query().trim().toLocaleLowerCase('tr');
    if (!q) return this.matches();
    return this.matches().filter(m => [m.homeTeam, m.awayTeam, m.league, m.country].some(s => s.toLocaleLowerCase('tr').includes(q)));
  });

  // Lig seçenekleri: aramaya uyan maçlardan, maç sayısıyla birlikte; önce favoriler, sonra alfabetik.
  // Seçili ligler aramada hiç maç içermese de listede (0) olarak kalır; yoksa seçim görünmez olurdu.
  protected readonly leagueOptions = computed(() => {
    const counts = new Map<string, number>();
    for (const m of this.searched()) counts.set(leagueKey(m), (counts.get(leagueKey(m)) ?? 0) + 1);
    for (const l of this.leagues()) if (!counts.has(l)) counts.set(l, 0);
    const favorites = this.favorites();
    const fav = (k: string) => (favorites.includes(k) ? 0 : 1);
    return [...counts].sort(([a], [b]) => fav(a) - fav(b) || a.localeCompare(b, this.i18n.lang()));
  });

  protected readonly filtered = computed(() => {
    const leagues = this.leagues();
    return leagues.length > 0 ? this.searched().filter(m => leagues.includes(leagueKey(m))) : this.searched();
  });

  // Bağlantıyla açılan maç o anki listede olmayabilir (başka gün, liste henüz yüklenmedi); bilgisi tahminden alınır.
  protected readonly selected = computed(() => {
    const id = this.selectedId();
    const f = this.forecast();
    return this.matches().find(m => m.id === id) ?? (f?.match.id === id ? f.match : null);
  });

  // Hızlı art arda tıklamalarda eski cevabın yenisini ezmemesi için
  private requestSeq = 0;

  constructor() {
    // Refresh token da geçersizse (süresi doldu, iptal edildi) api.ts haber verir: login ekranına dön.
    onSessionExpired(() => this.loggedIn.set(false));

    // Giriş yapılınca üst barda gösterilecek ad ve roller alınır
    effect(onCleanup => {
      if (!this.loggedIn()) return;
      let cancelled = false;
      getMe().then(data => { if (!cancelled) this.me.set(data); }).catch(() => { if (!cancelled) this.meFailed.set(true); });
      onCleanup(() => { cancelled = true; });
    });

    // Favori ligler kullanıcıya özgüdür; misafirde (paylaşılan hesap) yoktur. Yüklenemezse filtre favorisiz çalışır.
    effect(onCleanup => {
      if (!this.me() || this.isGuest()) return;
      let cancelled = false;
      getFavoriteLeagues().then(data => { if (!cancelled) this.favorites.set(data); }).catch(() => {});
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

    // Misafirin bugün tek maçı varsa beklemeden o maçın tahmini açılır (listeden seçim gerekmez).
    // Yalnızca hiç maç seçilmemişken çalışır; arama sonucu tek maça düşünce tetiklenmesin diye tüm liste (matches) kullanılır.
    effect(() => {
      const state = this.matchesState();
      const matches = this.matches();
      if (!this.isGuest() || state.loading || state.error || this.selectedId() !== null || this.routeMatchId() !== null || matches.length !== 1) return;
      // Geçmişe kayıt eklenmez: geri tuşu kullanıcıyı boş ekrana değil, bir önceki sayfaya götürsün.
      replaceHash(matchHash(matches[0].id));
      this.hash.set(location.hash);
    });

    // Adres değişince ekran ona uyar: #match/<id> o maçı açar; adres boşalırsa (geri tuşu) seçim kalkar.
    // Yönetim açıkken (#admin) seçim korunur ki geri dönüldüğünde aynı maç görünsün.
    effect(() => {
      if (!this.loggedIn()) return;
      const id = this.routeMatchId();
      const admin = this.adminRoute().admin;
      untracked(() => {
        if (id !== null) {
          if (id !== this.selectedId()) this.analyze(id);
        } else if (!admin && this.selectedId() !== null) {
          this.clearSelection();
        }
      });
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
    this.resetHash();
    this.loggedIn.set(false);
    this.me.set(null);
    this.meFailed.set(false);
    this.favorites.set([]);
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

  // Logo/site adı: admin panelinden çıkar, bugünün maçlarına döner, seçimi ve aramayı sıfırlar.
  // Lig filtresi ve favoriler korunur (kullanıcının kendi tercihi). Devam eden analiz cevabı yok sayılır.
  protected goHome(e: Event) {
    e.preventDefault();
    this.resetHash();
    this.clearSelection();
    this.date.set(todayInIstanbul());
    this.query.set('');
  }

  // Seçili maç kapatılır; devam eden analiz cevabı gelirse yok sayılır.
  private clearSelection() {
    this.requestSeq++;
    this.selectedId.set(null);
    this.forecast.set(null);
    this.forecastState.set({ loading: false });
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

  // Yıldız: ekranda hemen güncellenir; API hata verirse eski haline döner.
  protected toggleFavorite(key: string) {
    const wasFavorite = this.favorites().includes(key);
    this.favorites.update(prev => (wasFavorite ? prev.filter(l => l !== key) : [...prev, key]));
    (wasFavorite ? removeFavoriteLeague(key) : addFavoriteLeague(key))
      .then(data => this.favorites.set(data))
      .catch(() => this.favorites.update(prev => (wasFavorite ? [...prev, key] : prev.filter(l => l !== key))));
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
      // Bağlantı veya geri tuşuyla başka günün maçı açıldıysa liste o güne geçer (maç listede seçili görünsün).
      if (!refresh) {
        const day = istanbulDate(new Date(result.match.kickoff));
        if (this.date() !== day) this.date.set(day);
      }
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
      this.cacheState.set({ clearing: false, message: this.i18n.t('cache.cleared', { n: deletedKeys }) });
      this.loadMatches(this.date(), false);
    } catch (e) {
      this.cacheState.set({ clearing: false, message: (e as Error).message, error: true });
    }
  }
}