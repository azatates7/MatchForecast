import { Component, computed, ElementRef, inject, input, output, signal } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';

/*
 * Çoklu lig / ülke filtresi. Native <select multiple> Ctrl+tık istediği için kutucuklu açılır liste kullanılır.
 * - "Tümü" işaretliyken filtre yoktur; bir lig seçilince "Tümü" kendiliğinden kalkar.
 * - "Tümü" seçilince (veya son lig de kaldırılınca) lig filtreleri temizlenir.
 * - "Favori ligler": listede olan favori ligleri seçer; tam olarak onlar seçiliyken işaretli görünür, tekrar tıklanınca "Tümü"ye döner.
 * - Her ligin yanındaki yıldız ligi favorilere ekler/çıkarır (seçimi değiştirmez).
 * Dışarı tıklayınca veya Escape ile kapanır; lig seçerken açık kalır ki birden fazla lig art arda seçilebilsin.
 */
@Component({
  selector: 'app-league-filter',
  templateUrl: './league-filter.html',
  host: {
    '(document:mousedown)': 'onDocumentDown($event)',
    '(document:keydown.escape)': 'open.set(false)',
  },
})
export class LeagueFilter {
  /** [lig anahtarı, maç sayısı] çiftleri; aramaya uyan maçlardan, alfabetik */
  readonly options = input.required<[string, number][]>();
  /** Seçili lig anahtarları; boş dizi = "Tümü" (filtre yok) */
  readonly selected = input.required<string[]>();
  /** Aramaya uyan toplam maç sayısı ("Tüm ligler (n)") */
  readonly total = input.required<number>();
  /** Seçili liglerdeki maç sayısı ("Seçili Ligler (n)") */
  readonly selectedCount = input.required<number>();

  /** Kullanıcının favori lig anahtarları */
  readonly favorites = input<string[]>([]);

  readonly selectAll = output<void>();
  readonly toggle = output<string>();
  /** "Favori ligler" seçilince: listede olan favori liglerin anahtarları */
  readonly selectFavorites = output<string[]>();
  readonly toggleFavorite = output<string>();

  protected readonly star = 'M12 17.27 18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z';
  protected readonly starOutline = 'M22 9.24l-7.19-.62L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21 12 17.27 18.18 21l-1.63-7.03L22 9.24zM12 15.4l-3.76 2.27 1-4.28-3.32-2.88 4.38-.38L12 6.1l1.71 4.04 4.38.38-3.32 2.88 1 4.28L12 15.4z';

  private readonly favoriteOptions = computed(() => this.options().filter(([key]) => this.favorites().includes(key)));
  protected readonly favoriteKeys = computed(() => this.favoriteOptions().map(([key]) => key));
  protected readonly favoriteCount = computed(() => this.favoriteOptions().reduce((sum, [, count]) => sum + count, 0));
  protected readonly favoritesActive = computed(() => {
    const keys = this.favoriteKeys();
    const selected = this.selected();
    return keys.length > 0 && selected.length === keys.length && keys.every(k => selected.includes(k));
  });

  protected readonly label = computed(() =>
    this.selected().length === 0
      ? this.i18n.t('league.allLeagues', { n: this.total() })
      : this.favoritesActive()
        ? this.i18n.t('league.favoriteLeagues', { n: this.selectedCount() })
        : this.i18n.t('league.selectedLeagues', { n: this.selectedCount() }));

  protected readonly open = signal(false);
  protected readonly i18n = inject(I18nService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected onDocumentDown(e: MouseEvent) {
    if (this.open() && !this.host.nativeElement.contains(e.target as Node)) this.open.set(false);
  }

  protected isSelected(key: string): boolean {
    return this.selected().includes(key);
  }

  protected isFavorite(key: string): boolean {
    return this.favorites().includes(key);
  }

  protected onFavoritesChange() {
    if (this.favoritesActive()) this.selectAll.emit();
    else this.selectFavorites.emit(this.favoriteKeys());
  }

  // Label içindeki buton: tıklama kutucuğu değiştirmesin diye varsayılan davranış engellenir.
  protected onStarClick(e: MouseEvent, key: string) {
    e.preventDefault();
    this.toggleFavorite.emit(key);
  }
}