import { Component, computed, inject, input, signal } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { getGroups, getRoles, getUsers } from '../core/api';
import type { GroupResponse, MeResponse, RoleResponse, UserResponse } from '../core/types';
import { GroupDialog } from './admin-group-dialog';

/*
 * Gruplar sekmesi (groups.manage). Gruba verilen roller her üyeye geçer. Rol ve kullanıcı listeleri de yüklenir
 * (GET /api/roles ve /api/users groups.manage ile okunabilir). Admin rolü veren gruplarda silme / rol kaldırma /
 * üye çıkarma kuralları API'dedir (kendi Admin'liğini kaldırma, son Admin); mesajı diyalogda gösterilir.
 */
@Component({
  selector: 'app-groups-tab',
  imports: [GroupDialog],
  templateUrl: './groups-tab.html',
})
export class GroupsTab {
  readonly me = input.required<MeResponse>();

  protected readonly i18n = inject(I18nService);

  protected readonly groups = signal<GroupResponse[]>([]);
  protected readonly roles = signal<RoleResponse[]>([]);
  protected readonly users = signal<UserResponse[]>([]);
  protected readonly state = signal<{ loading: boolean; error?: string }>({ loading: true });
  // Açık diyalog: 'new' = yeni grup, sayı = düzenlenen grubun id'si
  protected readonly editing = signal<number | 'new' | null>(null);

  protected readonly editingGroup = computed(() => {
    const id = this.editing();
    return typeof id === 'number' ? this.groups().find(g => g.id === id) ?? null : null;
  });

  // Diyalog, açılan grup değişince yeniden oluşturulsun diye tek elemanlı liste (@for track ile); React'teki key karşılığı.
  protected readonly dialogKeys = computed(() => {
    const id = this.editing();
    return id === 'new' || this.editingGroup() ? [id] : [];
  });

  constructor() {
    this.reload();
  }

  protected roleNames(ids: number[]): string[] {
    return this.roles().filter(r => ids.includes(r.id)).map(r => r.name);
  }

  protected async reload() {
    try {
      const [g, r, u] = await Promise.all([getGroups(), getRoles(), getUsers()]);
      this.groups.set(g);
      this.roles.set(r);
      this.users.set(u);
      this.state.set({ loading: false });
    } catch (e) {
      this.state.set({ loading: false, error: (e as Error).message });
    }
  }

  protected async onCreated(id: number) {
    await this.reload();
    this.editing.set(id);
  }

  protected async onDeleted() {
    this.editing.set(null);
    await this.reload();
  }
}