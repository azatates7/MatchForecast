import { afterRenderEffect, Component, computed, ElementRef, inject, input, viewChild } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { adminHash, visibleAdminTabs, type AdminTab } from '../core/admin-view';
import type { MeResponse } from '../core/types';
import { UsersTab } from './admin-users-tab';
import { RolesTab } from './admin-roles-tab';
import { GroupsTab } from './admin-groups-tab';
import { PermissionsTab } from './admin-permissions-tab';
import { UsageTab } from './admin-usage-tab';
import { AccuracyTab } from './admin-accuracy-tab';

/*
 * Yönetim paneli iskeleti: yetkiye göre filtrelenmiş sekmeler (Kullanıcılar, Roller, Gruplar, Yetkiler, AI kullanımı, Tahmin isabeti).
 * Sekmeler bağlantıdır (#admin/<sekme>); tarayıcının geri/ileri düğmeleri sekmeler arasında çalışır.
 * Sekmeler: Kullanıcılar, Roller, Gruplar, Yetkiler (her biri kendi bileşeninde, components/admin-*).
 */
@Component({
  selector: 'app-admin-panel',
  imports: [UsersTab, RolesTab, GroupsTab, PermissionsTab, UsageTab, AccuracyTab],
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
  private readonly nav = viewChild<ElementRef<HTMLElement>>('nav');

  constructor() {
    // Dar ekranda sekmeler sığmayınca çubuk yatay kayar; aktif sekme görünür alana (ortaya) getirilir.
    // Yalnızca çubuğun scrollLeft'i değişir, sayfa dikey kaymaz (scrollIntoView'in aksine).
    afterRenderEffect(() => {
      this.active();
      const nav = this.nav()?.nativeElement;
      const el = nav?.querySelector<HTMLElement>('[aria-selected="true"]');
      if (!nav || !el) return;
      const offset = el.getBoundingClientRect().left - nav.getBoundingClientRect().left;
      nav.scrollLeft += offset - (nav.clientWidth - el.offsetWidth) / 2;
    });
  }
}