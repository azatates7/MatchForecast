import { Component, ElementRef, inject, input, output, signal } from '@angular/core';

/*
 * Çoklu lig / ülke filtresi. Native <select multiple> Ctrl+tık istediği için kutucuklu açılır liste kullanılır.
 * - "Tümü" işaretliyken filtre yoktur; bir lig seçilince "Tümü" kendiliğinden kalkar.
 * - "Tümü" seçilince (veya son lig de kaldırılınca) lig filtreleri temizlenir.
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

  readonly selectAll = output<void>();
  readonly toggle = output<string>();

  protected readonly open = signal(false);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected onDocumentDown(e: MouseEvent) {
    if (this.open() && !this.host.nativeElement.contains(e.target as Node)) this.open.set(false);
  }

  protected isSelected(key: string): boolean {
    return this.selected().includes(key);
  }
}