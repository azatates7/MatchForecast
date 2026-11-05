import { Component, computed, inject, signal } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { getPermissions, getRoles } from '../core/api';
import type { PermissionResponse, RoleResponse } from '../core/types';
import { PermissionDialog } from './admin-permissions-tab';

/*
 * Yetkiler sekmesi (permissions.manage). Sistem yetkileri kodun kontrol ettiği anahtarlardır (adı/açıklaması
 * değiştirilebilir, anahtarı değişmez, silinemez); panelden özel yetki eklenebilir. Rollere atama Roller sekmesindedir.
 * Rol adları için rol listesi de yüklenir (GET /api/roles permissions.manage ile okunabilir).
 */
@Component({
  selector: 'app-permissions-tab',
  imports: [PermissionDialog],
  templateUrl: './admin-permissions-tab.html',
})
export class PermissionsTab {
  protected readonly i18n = inject(I18nService);

  protected readonly permissions = signal<PermissionResponse[]>([]);
  protected readonly roles = signal<RoleResponse[]>([]);
  protected readonly state = signal<{ loading: boolean; error?: string }>({ loading: true });
  protected readonly query = signal('');
  // Açık diyalog: 'new' = yeni yetki, sayı = düzenlenen yetkinin id'si
  protected readonly editing = signal<number | 'new' | null>(null);

  protected readonly filtered = computed(() => {
    const q = this.query().trim().toLocaleLowerCase('tr');
    const sorted = [...this.permissions()].sort((a, b) => a.key.localeCompare(b.key));
    if (!q) return sorted;
    return sorted.filter(p => [p.key, p.name, p.nameEn, p.description ?? ''].some(s => s.toLocaleLowerCase('tr').includes(q)));
  });

  protected readonly editingPermission = computed(() => {
    const id = this.editing();
    return typeof id === 'number' ? this.permissions().find(p => p.id === id) ?? null : null;
  });

  // Diyalog, açılan yetki değişince yeniden oluşturulsun diye tek elemanlı liste (@for track ile); React'teki key karşılığı.
  protected readonly dialogKeys = computed(() => {
    const id = this.editing();
    return id === 'new' || this.editingPermission() ? [id] : [];
  });

  constructor() {
    this.reload();
  }

  protected roleNames(ids: number[]): string[] {
    return this.roles().filter(r => ids.includes(r.id)).map(r => r.name);
  }

  protected label(p: PermissionResponse): string {
    return this.i18n.lang() === 'en' ? p.nameEn : p.name;
  }

  protected async reload() {
    try {
      const [p, r] = await Promise.all([getPermissions(), getRoles()]);
      this.permissions.set(p);
      this.roles.set(r);
      this.state.set({ loading: false });
    } catch (e) {
      this.state.set({ loading: false, error: (e as Error).message });
    }
  }

  protected async onDone() {
    this.editing.set(null);
    await this.reload();
  }
}