import { Component, computed, inject, signal } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { getPermissions, getRoles } from '../core/api';
import type { PermissionResponse, RoleResponse } from '../core/types';
import { RoleDialog } from './admin-role-dialog';

/*
 * Roller sekmesi (roles.manage). Rol oluşturma, yeniden adlandırma, silme ve role yetki atama burada yapılır.
 * Yetki listesi de yüklenir (GET /api/permissions roles.manage ile okunabilir). Sistem rolleri (Admin, User, Guest)
 * yeniden adlandırılamaz/silinemez; Admin tüm yetkilere sahiptir ve yetki listesi düzenlenemez. Kurallar API'dedir.
 */
@Component({
  selector: 'app-roles-tab',
  imports: [RoleDialog],
  templateUrl: './roles-tab.html',
})
export class RolesTab {
  protected readonly i18n = inject(I18nService);

  protected readonly roles = signal<RoleResponse[]>([]);
  protected readonly permissions = signal<PermissionResponse[]>([]);
  protected readonly state = signal<{ loading: boolean; error?: string }>({ loading: true });
  // Açık diyalog: 'new' = yeni rol, sayı = düzenlenen rolün id'si
  protected readonly editing = signal<number | 'new' | null>(null);

  protected readonly editingRole = computed(() => {
    const id = this.editing();
    return typeof id === 'number' ? this.roles().find(r => r.id === id) ?? null : null;
  });

  // Diyalog, açılan rol değişince yeniden oluşturulsun diye tek elemanlı liste (@for track ile); React'teki key karşılığı.
  protected readonly dialogKeys = computed(() => {
    const id = this.editing();
    return id === 'new' || this.editingRole() ? [id] : [];
  });

  constructor() {
    this.reload();
  }

  protected async reload() {
    try {
      const [r, p] = await Promise.all([getRoles(), getPermissions()]);
      this.roles.set(r);
      this.permissions.set(p);
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