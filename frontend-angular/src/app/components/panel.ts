import { Component, computed, inject, input } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { adminHash, visibleAdminTabs, type AdminTab } from '../core/admin-view';
import type { MeResponse } from '../core/types';
import { UsersTab } from './users-tab';

/*
 * Yönetim paneli iskeleti: yetkiye göre filtrelenmiş sekmeler (Kullanıcılar, Roller, Gruplar, Yetkiler).
 * Sekmeler bağlantıdır (#admin/<sekme>); tarayıcının geri/ileri düğmeleri sekmeler arasında çalışır.
 * Kullanıcılar sekmesi hazır (8.4); Roller, Gruplar ve Yetkiler sonraki adımlarda (8.5–8.7) eklenecek.
 */
@Component({
  selector: 'app-admin-panel',
  imports: [UsersTab],
  templateUrl: './admin-panel.html',
})
export class AdminPanel {
  readonly me = input.required<MeResponse>();
  /** Adresteki sekme; yoksa veya kullanıcı göremiyorsa ilk görünen sekme açılır */
  readonly tab = input<AdminTab | null>(null);

  protected readonly i18n = inject(I18nService);
  protected readonly adminHash = adminHash;
  protected readonly tabs = computed(() => visibleAdminTabs(this.me()));
  protected readonly active = computed(() => this.tabs().find(x => x.id === this.tab()) ?? this.tabs()[0]);
}