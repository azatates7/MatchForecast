import { NgTemplateOutlet } from '@angular/common';
import { Component, ElementRef, afterNextRender, computed, inject, input, output, signal, viewChild, type OnInit, type WritableSignal } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { createUser, deleteUser, generatePassword, setUserGroups, setUserPassword, setUserRoles, updateUser } from '../core/api';
import type { GroupResponse, MeResponse, RoleResponse, UserResponse } from '../core/types';
import { idle, runAction, type ActionState } from './admin-action';

const idsOf = <T extends { id: number; name: string }>(all: T[], names: string[]) =>
  all.filter(x => names.includes(x.name)).map(x => x.id);

/*
 * Kullanıcı diyaloğu (native <dialog>; Esc ile kapanır). Yeni kullanıcıda yalnızca oluşturma formu vardır; oluşturulunca
 * üst bileşen aynı kullanıcı için düzenleme diyaloğunu açar ki rol ve grup hemen atanabilsin. Düzenlemede bölümler ayrı
 * ayrı kaydedilir: profil, roller, gruplar, şifre, silme. users.manage yoksa her şey salt okunurdur (fieldset disabled, düğme yok).
 */
@Component({
  selector: 'app-user-dialog',
  imports: [NgTemplateOutlet],
  templateUrl: './admin-user-dialog.html',
})
export class UserDialog implements OnInit {
  readonly me = input.required<MeResponse>();
  /** null = yeni kullanıcı */
  readonly user = input<UserResponse | null>(null);
  readonly roles = input.required<RoleResponse[]>();
  readonly groups = input.required<GroupResponse[]>();
  readonly canManage = input(false);

  readonly changed = output<void>();
  readonly created = output<number>();
  readonly deleted = output<void>();
  readonly closed = output<void>();

  protected readonly i18n = inject(I18nService);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  protected readonly isSelf = computed(() => {
    const user = this.user();
    return !!user && user.userName.toLowerCase() === this.me().userName.toLowerCase();
  });
  protected readonly isGuest = computed(() => !!this.user()?.roles.includes('Guest'));

  // Profil / yeni kullanıcı formu
  protected readonly userName = signal('');
  protected readonly email = signal('');
  protected readonly displayName = signal('');
  protected readonly isActive = signal(true);
  protected readonly password = signal('');
  protected readonly showPassword = signal(false);
  protected readonly generating = signal(false);

  protected readonly roleIds = signal<number[]>([]);
  protected readonly groupIds = signal<number[]>([]);
  protected readonly deleteConfirm = signal('');

  // Her bölümün kendi durum satırı olsun diye (profil kaydedilirken rol bölümündeki mesaj silinmesin).
  protected readonly profile = signal<ActionState>(idle());
  protected readonly roleAction = signal<ActionState>(idle());
  protected readonly groupAction = signal<ActionState>(idle());
  protected readonly passwordAction = signal<ActionState>(idle());
  protected readonly deleteAction = signal<ActionState>(idle());

  constructor() {
    afterNextRender(() => this.dialog().nativeElement.showModal());
  }

  ngOnInit() {
    const user = this.user();
    this.email.set(user?.email ?? '');
    this.displayName.set(user?.displayName ?? '');
    this.isActive.set(user?.isActive ?? true);
    this.roleIds.set(idsOf(this.roles(), user?.roles ?? []));
    this.groupIds.set(idsOf(this.groups(), user?.groups ?? []));
  }

  protected close() {
    this.dialog().nativeElement.close();
  }

  protected toggle(list: WritableSignal<number[]>, id: number) {
    list.update(ids => (ids.includes(id) ? ids.filter(x => x !== id) : [...ids, id]));
  }

  private run(state: WritableSignal<ActionState>, action: () => Promise<unknown>, info?: string) {
    return runAction(state, action, info);
  }

  protected async generate() {
    this.generating.set(true);
    try {
      this.password.set((await generatePassword(16)).password);
      this.showPassword.set(true);
    } catch { /* üretilemezse kullanıcı elle yazar */ }
    this.generating.set(false);
  }

  protected submitCreate(e: Event) {
    e.preventDefault();
    this.run(this.profile, async () => {
      const created = await createUser({
        userName: this.userName().trim(),
        email: this.email().trim(),
        displayName: this.displayName().trim() || null,
        password: this.password(),
      });
      this.created.emit(created.id);
    });
  }

  protected submitProfile(e: Event) {
    e.preventDefault();
    const user = this.user();
    if (!user) return;
    this.run(this.profile, async () => {
      await updateUser(user.id, { email: this.email().trim(), displayName: this.displayName().trim() || null, isActive: this.isActive() });
      this.changed.emit();
    }, this.i18n.t('admin.saved'));
  }

  protected saveRoles(user: UserResponse) {
    this.run(this.roleAction, async () => {
      await setUserRoles(user.id, this.roleIds());
      this.changed.emit();
    }, this.i18n.t('admin.saved'));
  }

  protected saveGroups(user: UserResponse) {
    this.run(this.groupAction, async () => {
      await setUserGroups(user.id, this.groupIds());
      this.changed.emit();
    }, this.i18n.t('admin.saved'));
  }

  protected submitPassword(e: Event) {
    e.preventDefault();
    const user = this.user();
    if (!user) return;
    this.run(this.passwordAction, async () => {
      await setUserPassword(user.id, this.password());
      this.password.set('');
    }, this.i18n.t('admin.user.passwordSet'));
  }

  protected remove(user: UserResponse) {
    this.run(this.deleteAction, async () => {
      await deleteUser(user.id);
      this.deleted.emit();
    });
  }
}