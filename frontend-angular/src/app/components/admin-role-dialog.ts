import { NgTemplateOutlet } from '@angular/common';
import { Component, ElementRef, afterNextRender, computed, inject, input, output, signal, viewChild, type OnInit } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { createRole, deleteRole, setRolePermissions, updateRole } from '../core/api';
import type { PermissionResponse, RoleResponse } from '../core/types';
import { idle, runAction, type ActionState } from './admin-action';

// Yetkiler alanına (anahtarın ilk parçası: "users.view" → "users") göre gruplanır; alanlar ve yetkiler anahtara göre sıralı.
const groupByArea = (permissions: PermissionResponse[]) => {
  const areas = new Map<string, PermissionResponse[]>();
  for (const p of [...permissions].sort((a, b) => a.key.localeCompare(b.key))) {
    const area = p.key.split('.')[0];
    areas.set(area, [...(areas.get(area) ?? []), p]);
  }
  return [...areas];
};

/*
 * Rol diyaloğu (native <dialog>). Yeni rolde yalnızca ad/açıklama; oluşturulunca üst bileşen aynı rol için düzenleme
 * diyaloğunu açar. Düzenlemede: bilgiler (sistem rolünde ad kilitli), yetkiler (Admin'de düzenlenemez), silme (sistem
 * rolünde yok). Atanmış bir rol silinirken atamalar da kaldırılır (force=true); bu, onay alanının üstünde açıkça yazılır.
 */
@Component({
  selector: 'app-role-dialog',
  imports: [NgTemplateOutlet],
  templateUrl: './role-dialog.html',
})
export class RoleDialog implements OnInit {
  /** null = yeni rol */
  readonly role = input<RoleResponse | null>(null);
  readonly permissions = input.required<PermissionResponse[]>();

  readonly changed = output<void>();
  readonly created = output<number>();
  readonly deleted = output<void>();
  readonly closed = output<void>();

  protected readonly i18n = inject(I18nService);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  protected readonly name = signal('');
  protected readonly description = signal('');
  protected readonly permissionIds = signal<number[]>([]);
  protected readonly deleteConfirm = signal('');

  protected readonly info = signal<ActionState>(idle());
  protected readonly permissionAction = signal<ActionState>(idle());
  protected readonly deleteAction = signal<ActionState>(idle());

  protected readonly areas = computed(() => groupByArea(this.permissions()));
  protected readonly assigned = computed(() => {
    const role = this.role();
    return role ? role.userCount + role.groupCount : 0;
  });

  constructor() {
    afterNextRender(() => this.dialog().nativeElement.showModal());
  }

  ngOnInit() {
    const role = this.role();
    this.name.set(role?.name ?? '');
    this.description.set(role?.description ?? '');
    this.permissionIds.set(role?.permissionIds ?? []);
  }

  protected close() {
    this.dialog().nativeElement.close();
  }

  protected label(p: PermissionResponse): string {
    return this.i18n.lang() === 'en' ? p.nameEn : p.name;
  }

  protected toggle(id: number) {
    this.permissionIds.update(ids => (ids.includes(id) ? ids.filter(x => x !== id) : [...ids, id]));
  }

  protected submitInfo(e: Event) {
    e.preventDefault();
    const role = this.role();
    const body = { name: this.name().trim(), description: this.description().trim() || null };
    if (!role) {
      runAction(this.info, async () => {
        const created = await createRole(body);
        this.created.emit(created.id);
      });
      return;
    }
    runAction(this.info, async () => {
      await updateRole(role.id, body);
      this.changed.emit();
    }, this.i18n.t('admin.saved'));
  }

  protected savePermissions(role: RoleResponse) {
    runAction(this.permissionAction, async () => {
      await setRolePermissions(role.id, this.permissionIds());
      this.changed.emit();
    }, this.i18n.t('admin.saved'));
  }

  protected remove(role: RoleResponse) {
    runAction(this.deleteAction, async () => {
      await deleteRole(role.id, this.assigned() > 0);
      this.deleted.emit();
    });
  }
}