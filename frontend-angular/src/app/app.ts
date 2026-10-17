import { Component, computed, effect, signal } from '@angular/core';
import { clearCache, generatePassword, getForecast, getMatches } from './core/api';
import type { ForecastResult, MatchSummary } from './core/types';
import { isLive } from './core/match-status';
import { MatchList } from './components/match-list';
import { ForecastPanel } from './components/forecast-panel';

// Canlı maç varken listenin arka planda yenilenme aralığı. API kotasını korumak için
// kısa tutulmadı; aradaki dakikalar minuteLabel içinde yerelde ilerletilir.
const LIVE_REFRESH_MS = 5 * 60_000;
// Ekrandaki dakika etiketinin güncellenme aralığı (ağ isteği yapmaz)
const CLOCK_TICK_MS = 30_000;
// Cache temizleme sonucunun ekranda kalma süresi
const CACHE_MSG_MS = 4_000;
// "Kopyalandı" bilgisinin ekranda kalma süresi
const COPIED_MSG_MS = 2_000;

const toIsoDate = (d: Date) => {
  const local = new Date(d.getTime() - d.getTimezoneOffset() * 60000);
  return local.toISOString().slice(0, 10);
};

const shiftDate = (iso: string, days: number) => {
  const d = new Date(`${iso}T12:00:00`);
  d.setDate(d.getDate() + days);
  return toIsoDate(d);
};

const leagueKey = (m: MatchSummary) => `${m.country} / ${m.league}`;

interface LoadState { loading: boolean; error?: string }
interface CacheState { clearing: boolean; message?: string; error?: boolean }
interface PasswordState { loading: boolean; password?: string; error?: string; copied?: boolean }

@Component({
  selector: 'app-root',
  imports: [MatchList, ForecastPanel],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  protected readonly date = signal(toIsoDate(new Date()));
  protected readonly query = signal('');
  protected readonly league = signal('');
  protected readonly matches = signal<MatchSummary[]>([]);
  protected readonly fetchedAt = signal(Date.now());
  protected readonly now = signal(Date.now());
  protected readonly matchesState = signal<LoadState>({ loading: true });

  protected readonly selectedId = signal<number | null>(null);
  protected readonly forecast = signal<ForecastResult | null>(null);
  protected readonly forecastState = signal<LoadState>({ loading: false });
  protected readonly cacheState = signal<CacheState>({ clearing: false });
  protected readonly passwordState = signal<PasswordState>({ loading: false });

  protected readonly hasLive = computed(() => this.matches().some(isLive));

  // Lig seçenekleri: yüklenen maçlardan, maç sayısıyla birlikte, alfabetik
  protected readonly leagueOptions = computed(() => {
    const counts = new Map<string, number>();
    for (const m of this.matches()) counts.set(leagueKey(m), (counts.get(leagueKey(m)) ?? 0) + 1);
    return [...counts].sort(([a], [b]) => a.localeCompare(b, 'tr'));
  });

  protected readonly filtered = computed(() => {
    const q = this.query().trim().toLocaleLowerCase('tr');
    const league = this.league();
    return this.matches().filter(m =>
      (!league || leagueKey(m) === league) &&
      (!q || [m.homeTeam, m.awayTeam, m.league, m.country].some(s => s.toLocaleLowerCase('tr').includes(q))));
  });

  protected readonly selected = computed(() => this.matches().find(m => m.id === this.selectedId()) ?? null);

  // Hızlı art arda tıklamalarda eski cevabın yenisini ezmemesi için
  private requestSeq = 0;

  constructor() {
    // Tarih değişince listeyi yükle ve lig filtresini sıfırla (yeni tarihte o lig olmayabilir)
    effect(onCleanup => {
      const date = this.date();
      let cancelled = false;
      this.league.set('');
      this.loadMatches(date, false, () => cancelled);
      onCleanup(() => { cancelled = true; });
    });

    // Canlı maç varsa: listeyi periyodik yenile ve dakika etiketini ilerlet
    effect(onCleanup => {
      if (!this.hasLive()) return;
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

    // "Kopyalandı" etiketi kısa süre sonra "Kopyala"ya döner
    effect(onCleanup => {
      if (!this.passwordState().copied) return;
      const timer = setTimeout(() => this.passwordState.update(s => ({ ...s, copied: false })), COPIED_MSG_MS);
      onCleanup(() => clearTimeout(timer));
    });
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

  // Şifre yalnızca signal'de tutulur (localStorage'a yazılmaz); yeni şifre istenince eskisinin yerine geçer.
  protected async handleGeneratePassword() {
    this.passwordState.set({ loading: true });
    try {
      const { password } = await generatePassword();
      this.passwordState.set({ loading: false, password });
    } catch (e) {
      this.passwordState.set({ loading: false, error: (e as Error).message });
    }
  }

  // Clipboard API yalnızca HTTPS/localhost'ta çalışır; başarısız olursa şifre zaten ekranda seçilebilir durumda.
  protected async handleCopyPassword() {
    const password = this.passwordState().password;
    if (!password) return;
    try {
      await navigator.clipboard.writeText(password);
      this.passwordState.update(s => ({ ...s, copied: true }));
    } catch {
      this.passwordState.update(s => ({ ...s, error: 'Kopyalanamadı; şifreyi seçip elle kopyalayın.' }));
    }
  }
}