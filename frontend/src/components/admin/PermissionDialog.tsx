import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useI18n } from '../../i18n/useI18n'
import { createPermission, deletePermission, updatePermission } from '../../api'
import type { PermissionResponse } from '../../types'
import { ActionStatus, Chips, useAction } from './AdminUI'

interface Props {
  /** null = yeni yetki */
  permission: PermissionResponse | null
  /** Bu yetkinin verildiği rollerin adları (Admin hariç; Admin her yetkiye örtük olarak sahiptir) */
  roleNames: string[]
  onChanged: () => Promise<void>
  /** Oluşturma veya silme bitti: diyalog kapanır, liste yenilenir */
  onDone: () => Promise<void>
  onClose: () => void
}

// API ile aynı biçim (PermissionKeyPattern): "alan.işlem", en az iki parça; harf, rakam ve '-'.
// HTML pattern 'v' bayrağıyla çalıştığı için karakter sınıfındaki '-' kaçışlı yazılır.
const KEY_PATTERN = '[a-zA-Z0-9\\-]+(\\.[a-zA-Z0-9\\-]+)+'

/*
 * Yetki diyaloğu (native <dialog>). Yeni yetkide anahtar + TR/EN ad + açıklama; oluşturulunca diyalog kapanır (rollere
 * atama Roller sekmesindedir). Düzenlemede sistem yetkisinin anahtarı kilitlidir ve silme bölümü yoktur; özel yetki
 * silinirken rollere yapılmış atamaları da kaldırılır.
 */
export default function PermissionDialog({ permission, roleNames, onChanged, onDone, onClose }: Props) {
  const { t } = useI18n()
  const dialog = useRef<HTMLDialogElement>(null)
  useEffect(() => { dialog.current?.showModal() }, [])

  const [key, setKey] = useState(permission?.key ?? '')
  const [name, setName] = useState(permission?.name ?? '')
  const [nameEn, setNameEn] = useState(permission?.nameEn ?? '')
  const [description, setDescription] = useState(permission?.description ?? '')
  const [deleteConfirm, setDeleteConfirm] = useState('')

  const info = useAction()
  const deleteAction = useAction()

  const submit = (e: FormEvent) => {
    e.preventDefault()
    const common = { name: name.trim(), nameEn: nameEn.trim(), description: description.trim() || null }
    if (!permission) {
      info.run(async () => { await createPermission({ key: key.trim(), ...common }); await onDone() })
      return
    }
    // Sistem yetkisinde anahtar gönderilmez (null = değişmez)
    const body = { key: permission.isSystem ? null : key.trim(), ...common }
    info.run(async () => { await updatePermission(permission.id, body); await onChanged() }, t('admin.saved'))
  }

  return (
    <dialog ref={dialog} className="admin-dialog" onClose={onClose} aria-labelledby="permission-dialog-title">
      <div className="admin-dialog-head">
        <h3 id="permission-dialog-title">{permission ? permission.key : t('admin.permissions.new')}</h3>
        <button type="button" className="cache-btn" onClick={() => dialog.current?.close()}>{t('admin.close')}</button>
      </div>

      <div className="admin-dialog-body">
        {permission?.isSystem && <p className="admin-hint admin-note">{t('admin.permission.systemNote')}</p>}
        {permission && !permission.isSystem && <p className="admin-hint admin-note">{t('admin.role.customHint')}</p>}

        <form className="admin-section" onSubmit={submit}>
          {permission && <h4>{t('admin.role.info')}</h4>}
          <label className="login-field">
            <span>{t('admin.permission.key')}</span>
            <input
              type="text"
              required
              maxLength={100}
              pattern={KEY_PATTERN}
              title={t('admin.permission.keyHint')}
              autoComplete="off"
              spellCheck={false}
              disabled={permission?.isSystem}
              value={key}
              onChange={e => setKey(e.target.value)}
            />
          </label>
          {!permission?.isSystem && <p className="admin-hint">{t('admin.permission.keyHint')}</p>}
          <div className="admin-form-grid">
            <label className="login-field">
              <span>{t('admin.permission.name')}</span>
              <input type="text" required maxLength={100} autoComplete="off" value={name} onChange={e => setName(e.target.value)} />
            </label>
            <label className="login-field">
              <span>{t('admin.permission.nameEn')}</span>
              <input type="text" required maxLength={100} autoComplete="off" value={nameEn} onChange={e => setNameEn(e.target.value)} />
            </label>
          </div>
          <label className="login-field">
            <span>{t('admin.role.description')}</span>
            <input type="text" maxLength={500} autoComplete="off" value={description} onChange={e => setDescription(e.target.value)} />
          </label>
          {!permission && <p className="admin-hint">{t('admin.permission.createHint')}</p>}
          <div className="admin-actions">
            <button type="submit" className="cache-btn" disabled={info.busy}>{t(permission ? 'admin.save' : 'admin.create')}</button>
            <ActionStatus state={info} />
          </div>
        </form>

        {permission && (
          <>
            <section className="admin-section">
              <h4>{t('admin.permission.roles')}</h4>
              <Chips items={roleNames} />
              <p className="admin-hint">{t('admin.permission.rolesHint')}</p>
            </section>

            {!permission.isSystem && (
              <section className="admin-section admin-danger">
                <h4>{t('admin.permission.delete')}</h4>
                {roleNames.length > 0 && (
                  <p className="admin-hint admin-note">{t('admin.permission.deleteWarning', { n: roleNames.length })}</p>
                )}
                <label className="login-field">
                  <span>{t('admin.permission.deleteHint', { key: permission.key })}</span>
                  <input type="text" autoComplete="off" spellCheck={false} value={deleteConfirm} onChange={e => setDeleteConfirm(e.target.value)} />
                </label>
                <div className="admin-actions">
                  <button
                    type="button"
                    className="cache-btn danger-btn"
                    disabled={deleteAction.busy || deleteConfirm !== permission.key}
                    onClick={() => deleteAction.run(async () => { await deletePermission(permission.id); await onDone() })}
                  >
                    {t('admin.user.deleteConfirm')}
                  </button>
                  <ActionStatus state={deleteAction} />
                </div>
              </section>
            )}
          </>
        )}
      </div>
    </dialog>
  )
}