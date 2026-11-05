import { Component, computed, inject, input, signal } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { getGroups, getRoles, getUsers } from '../core/api';
import { hasPermission, PERMISSIONS, type GroupResponse, type MeResponse, type RoleResponse, type UserResponse } from '../core/types';
import { UserDialog } from './admin-user-dialog';

/*
 * Kullanıcılar sekmesi (users.view). Değişiklik düğmeleri yalnızca users.manage yetkisiyle görünür; yetki yoksa
 * diyalog salt okunur açılır. Asıl kontrol API'dedir (kendini pasife alma, son Admin vb. kuralları API mesajıyla gösterilir).
 * Roller ve gruplar diyalogda id ile atanır; liste bu yüzden rol ve grup listelerini de yükler.
 */
@Component({
  selector: 'app-users-tab',
  imports: [UserDialog],
  templateUrl: './admin-users-tab.html',
})
export class UsersTab {
  readonly me = input.required<MeResponse>();

  protected readonly i18n = inject(I18nService);
  protected readonly canManage = computed(() => hasPermission(this.me(), PERMISSIONS.usersManage));

  protected readonly users = signal<UserResponse[]>([]);
  protected readonly roles = signal<RoleResponse[]>([]);
  protected readonly groups = signal<GroupResponse[]>([]);
  protected readonly state = signal<{ loading: boolean; error?: string }>({ loading: true });
  protected readonly query = signal('');
  // Açık diyalog: 'new' = yeni kullanıcı, sayı = düzenlenen kullanıcının id'si
  protected readonly editing = signal<number | 'new' | null>(null);

  protected readonly filtered = computed(() => {
    const q = this.query().trim().toLocaleLowerCase('tr');
    if (!q) return this.users();
    return this.users().filter(u => [u.userName, u.displayName ?? '', u.email].some(s => s.toLocaleLowerCase('tr').includes(q)));
  });

  protected readonly editingUser = computed(() => {
    const id = this.editing();
    return typeof id === 'number' ? this.users().find(u => u.id === id) ?? null : null;
  });

  // Diyalog, açılan kullanıcı değişince yeniden oluşturulsun diye tek elemanlı liste (@for track ile); React'teki key karşılığı.
  protected readonly dialogKeys = computed(() => {
    const id = this.editing();
    return id === 'new' || this.editingUser() ? [id] : [];
  });

  constructor() {
    this.reload();
  }

  protected async reload() {
    try {
      const [u, r, g] = await Promise.all([getUsers(), getRoles(), getGroups()]);
      this.users.set(u);
      this.roles.set(r);
      this.groups.set(g);
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

  protected formatDate(iso: string | null): string {
    return iso ? new Date(iso).toLocaleString(this.i18n.locale(), { dateStyle: 'medium', timeStyle: 'short' }) : '—';
  }
}