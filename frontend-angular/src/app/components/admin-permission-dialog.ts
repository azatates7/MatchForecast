import { NgTemplateOutlet } from '@angular/common';
import { Component, ElementRef, afterNextRender, inject, input, output, signal, viewChild, type OnInit } from '@angular/core';
import { I18nService } from '../core/i18n/i18n.service';
import { createPermission, deletePermission, updatePermission } from '../core/api';
import type { PermissionResponse } from '../core/types';
import { idle, runAction, type ActionState } from './admin-action';

/*
 * Yetki diyaloğu (native <dialog>). Yeni yetkide anahtar + TR/EN ad + açıklama; oluşturulunca diyalog kapanır (rollere
 * atama Roller sekmesindedir). Düzenlemede sistem yetkisinin anahtarı kilitlidir ve silme bölümü yoktur; özel yetki
 * silinirken rollere yapılmış atamaları da kaldırılır.
 */
@Component({
  selector: 'app-permission-dialog',
  imports: [NgTemplateOutlet],
  templateUrl: './admin-permission-dialog.html',
})
export class PermissionDialog implements OnInit {
  /** null = yeni yetki */
  readonly permission = input<PermissionResponse | null>(null);
  /** Bu yetkinin verildiği rollerin adları (Admin hariç; Admin her yetkiye örtük olarak sahiptir) */
  readonly roleNames = input<string[]>([]);

  readonly changed = output<void>();
  /** Oluşturma veya silme bitti: üst bileşen diyaloğu kapatır, listeyi yeniler */
  readonly done = output<void>();
  readonly closed = output<void>();

  // API ile aynı biçim (PermissionKeyPattern): "alan.işlem", en az iki parça; harf, rakam ve '-'.
  // HTML pattern 'v' bayrağıyla çalıştığı için karakter sınıfındaki '-' kaçışlı yazılır.
  protected readonly keyPattern = '[a-zA-Z0-9\\-]+(\\.[a-zA-Z0-9\\-]+)+';

  protected readonly i18n = inject(I18nService);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  protected readonly key = signal('');
  protected readonly name = signal('');
  protected readonly nameEn = signal('');
  protected readonly description = signal('');
  protected readonly deleteConfirm = signal('');

  protected readonly info = signal<ActionState>(idle());
  protected readonly deleteAction = signal<ActionState>(idle());

  constructor() {
    afterNextRender(() => this.dialog().nativeElement.showModal());
  }

  ngOnInit() {
    const permission = this.permission();
    this.key.set(permission?.key ?? '');
    this.name.set(permission?.name ?? '');
    this.nameEn.set(permission?.nameEn ?? '');
    this.description.set(permission?.description ?? '');
  }

  protected close() {
    this.dialog().nativeElement.close();
  }

  protected submit(e: Event) {
    e.preventDefault();
    const permission = this.permission();
    const common = { name: this.name().trim(), nameEn: this.nameEn().trim(), description: this.description().trim() || null };
    if (!permission) {
      runAction(this.info, async () => {
        await createPermission({ key: this.key().trim(), ...common });
        this.done.emit();
      });
      return;
    }
    // Sistem yetkisinde anahtar gönderilmez (null = değişmez)
    const body = { key: permission.isSystem ? null : this.key().trim(), ...common };
    runAction(this.info, async () => {
      await updatePermission(permission.id, body);
      this.changed.emit();
    }, this.i18n.t('admin.saved'));
  }

  protected remove(permission: PermissionResponse) {
    runAction(this.deleteAction, async () => {
      await deletePermission(permission.id);
      this.done.emit();
    });
  }
}