import { NgTemplateOutlet } from '@angular/common';
import { Component, ElementRef, afterNextRender, computed, inject, input, output, signal, viewChild, type OnInit, type WritableSignal } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { createGroup, deleteGroup, setGroupRoles, setGroupUsers, updateGroup } from '../core/api';
import type { GroupResponse, MeResponse, RoleResponse, UserResponse } from '../core/types';
import { idle, runAction, type ActionState } from './admin-action';

/*
 * Grup diyaloğu (native <dialog>). Yeni grupta yalnızca ad/açıklama; oluşturulunca üst bileşen aynı grup için düzenleme
 * diyaloğunu açar. Düzenlemede: bilgiler, roller (Admin seçiliyse üyelerin Admin olacağı uyarısı), üyeler (aranabilir
 * liste), silme. Kullanıcıların kendisi silinmez; yalnızca üyelikler ve grubun rol atamaları kaldırılır.
 */
@Component({
  selector: 'app-group-dialog',
  imports: [NgTemplateOutlet],
  templateUrl: './group-dialog.html',
})
export class GroupDialog implements OnInit {
  readonly me = input.required<MeResponse>();
  /** null = yeni grup */
  readonly group = input<GroupResponse | null>(null);
  readonly roles = input.required<RoleResponse[]>();
  readonly users = input.required<UserResponse[]>();

  readonly changed = output<void>();
  readonly created = output<number>();
  readonly deleted = output<void>();
  readonly closed = output<void>();

  protected readonly i18n = inject(I18nService);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  protected readonly name = signal('');
  protected readonly description = signal('');
  protected readonly roleIds = signal<number[]>([]);
  protected readonly memberIds = signal<number[]>([]);
  protected readonly memberQuery = signal('');
  protected readonly deleteConfirm = signal('');

  protected readonly info = signal<ActionState>(idle());
  protected readonly roleAction = signal<ActionState>(idle());
  protected readonly memberAction = signal<ActionState>(idle());
  protected readonly deleteAction = signal<ActionState>(idle());

  protected readonly grantsAdmin = computed(() => this.roles().some(r => r.hasAllPermissions && this.roleIds().includes(r.id)));

  // Üye listesi: aramaya uyanlar, önce (açılıştaki) üyeler, sonra kullanıcı adına göre.
  protected readonly memberOptions = computed(() => {
    const q = this.memberQuery().trim().toLocaleLowerCase('tr');
    const members = this.group()?.memberIds ?? [];
    const matches = (u: UserResponse) => !q || [u.userName, u.displayName ?? ''].some(s => s.toLocaleLowerCase('tr').includes(q));
    const order = (u: UserResponse) => (members.includes(u.id) ? 0 : 1);
    return this.users().filter(matches).sort((a, b) => order(a) - order(b) || a.userName.localeCompare(b.userName));
  });

  constructor() {
    afterNextRender(() => this.dialog().nativeElement.showModal());
  }

  ngOnInit() {
    const group = this.group();
    this.name.set(group?.name ?? '');
    this.description.set(group?.description ?? '');
    this.roleIds.set(group?.roleIds ?? []);
    this.memberIds.set(group?.memberIds ?? []);
  }

  protected close() {
    this.dialog().nativeElement.close();
  }

  protected isMe(u: UserResponse): boolean {
    return u.userName.toLowerCase() === this.me().userName.toLowerCase();
  }

  protected toggle(list: WritableSignal<number[]>, id: number) {
    list.update(ids => (ids.includes(id) ? ids.filter(x => x !== id) : [...ids, id]));
  }

  protected submitInfo(e: Event) {
    e.preventDefault();
    const group = this.group();
    const body = { name: this.name().trim(), description: this.description().trim() || null };
    if (!group) {
      runAction(this.info, async () => {
        const created = await createGroup(body);
        this.created.emit(created.id);
      });
      return;
    }
    runAction(this.info, async () => {
      await updateGroup(group.id, body);
      this.changed.emit();
    }, this.i18n.t('admin.saved'));
  }

  protected saveRoles(group: GroupResponse) {
    runAction(this.roleAction, async () => {
      await setGroupRoles(group.id, this.roleIds());
      this.changed.emit();
    }, this.i18n.t('admin.saved'));
  }

  protected saveMembers(group: GroupResponse) {
    runAction(this.memberAction, async () => {
      await setGroupUsers(group.id, this.memberIds());
      this.changed.emit();
    }, this.i18n.t('admin.saved'));
  }

  protected remove(group: GroupResponse) {
    runAction(this.deleteAction, async () => {
      await deleteGroup(group.id);
      this.deleted.emit();
    });
  }
}