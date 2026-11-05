import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { useI18n } from '../../i18n/useI18n'
import { createRole, deleteRole, setRolePermissions, updateRole } from '../../api'
import type { PermissionResponse, RoleResponse } from '../../types'
import { ActionStatus, useAction } from './adminUi'

interface Props {
  /** null = yeni rol */
  role: RoleResponse | null
  permissions: PermissionResponse[]
  onChanged: () => Promise<void>
  onCreated: (id: number) => Promise<void>
  onDeleted: () => Promise<void>
  onClose: () => void
}

// Yetkiler alanına (anahtarın ilk parçası: "users.view" → "users") göre gruplanır; alanlar ve yetkiler anahtara göre sıralı.
const groupByArea = (permissions: PermissionResponse[]) => {
  const areas = new Map<string, PermissionResponse[]>()
  for (const p of [...permissions].sort((a, b) => a.key.localeCompare(b.key))) {
    const area = p.key.split('.')[0]
    areas.set(area, [...(areas.get(area) ?? []), p])
  }
  return [...areas]
}

/*
 * Rol diyaloğu (native <dialog>). Yeni rolde yalnızca ad/açıklama; oluşturulunca aynı diyalog düzenleme moduna geçer.
 * Düzenlemede: bilgiler (sistem rolünde ad kilitli), yetkiler (Admin'de düzenlenemez), silme (sistem rolünde yok).
 * Atanmış bir rol silinirken atamalar da kaldırılır (force=true); bu, onay alanının üstünde açıkça yazılır.
 */
export default function RoleDialog({ role, permissions, onChanged, onCreated, onDeleted, onClose }: Props) {
  const { t, lang } = useI18n()
  const dialog = useRef<HTMLDialogElement>(null)
  useEffect(() => { dialog.current?.showModal() }, [])

  const [name, setName] = useState(role?.name ?? '')
  const [description, setDescription] = useState(role?.description ?? '')
  const [permissionIds, setPermissionIds] = useState<number[]>(role?.permissionIds ?? [])
  const [deleteConfirm, setDeleteConfirm] = useState('')

  const info = useAction()
  const permissionAction = useAction()
  const deleteAction = useAction()

  const areas = useMemo(() => groupByArea(permissions), [permissions])
  const assigned = role ? role.userCount + role.groupCount : 0

  const toggle = (id: number) => setPermissionIds(ids => (ids.includes(id) ? ids.filter(x => x !== id) : [...ids, id]))

  const submitInfo = (e: FormEvent) => {
    e.preventDefault()
    const body = { name: name.trim(), description: description.trim() || null }
    if (!role) {
      info.run(async () => { const created = await createRole(body); await onCreated(created.id) })
      return
    }
    info.run(async () => { await updateRole(role.id, body); await onChanged() }, t('admin.saved'))
  }

  return (
    <dialog ref={dialog} className="admin-dialog" onClose={onClose} aria-labelledby="role-dialog-title">
      <div className="admin-dialog-head">
        <h3 id="role-dialog-title">{role ? role.name : t('admin.roles.new')}</h3>
        <button type="button" className="cache-btn" onClick={() => dialog.current?.close()}>{t('admin.close')}</button>
      </div>

      <div className="admin-dialog-body">
        {role?.isSystem && <p className="admin-hint admin-note">{t('admin.role.systemNote')}</p>}

        {/* Bilgiler */}
        <form className="admin-section" onSubmit={submitInfo}>
          {role && <h4>{t('admin.role.info')}</h4>}
          <div className="admin-form-grid">
            <label className="login-field">
              <span>{t('admin.role.name')}</span>
              <input type="text" required maxLength={50} autoComplete="off" disabled={role?.isSystem} value={name} onChange={e => setName(e.target.value)} />
            </label>
            <label className="login-field">
              <span>{t('admin.role.description')}</span>
              <input type="text" maxLength={250} autoComplete="off" value={description} onChange={e => setDescription(e.target.value)} />
            </label>
          </div>
          {!role && <p className="admin-hint">{t('admin.role.createHint')}</p>}
          <div className="admin-actions">
            <button type="submit" className="cache-btn" disabled={info.busy}>{t(role ? 'admin.save' : 'admin.create')}</button>
            <ActionStatus state={info} />
          </div>
        </form>

        {role && (
          <>
            {/* Yetkiler */}
            <section className="admin-section">
              <h4>{t('admin.role.permissions')}</h4>
              {role.hasAllPermissions ? (
                <p className="admin-hint admin-note">{t('admin.role.allPermissionsNote')}</p>
              ) : permissions.length === 0 ? (
                <p className="admin-hint">{t('admin.role.noPermissions')}</p>
              ) : (
                <>
                  {areas.map(([area, items]) => (
                    <fieldset key={area} className="admin-fieldset admin-area">
                      <legend className="admin-area-title">{area}</legend>
                      {items.map(p => (
                        <label key={p.id} className="admin-check admin-permission">
                          <input type="checkbox" checked={permissionIds.includes(p.id)} onChange={() => toggle(p.id)} />
                          <span>
                            {lang === 'en' ? p.nameEn : p.name}
                            <code className="admin-key">{p.key}</code>
                            {!p.isSystem && <span className="badge badge-off" title={t('admin.role.customHint')}>{t('admin.role.custom')}</span>}
                          </span>
                        </label>
                      ))}
                    </fieldset>
                  ))}
                  <p className="admin-hint">{t('admin.role.permissionsHint')}</p>
                  <div className="admin-actions">
                    <button
                      type="button"
                      className="cache-btn"
                      disabled={permissionAction.busy}
                      onClick={() => permissionAction.run(async () => { await setRolePermissions(role.id, permissionIds); await onChanged() }, t('admin.saved'))}
                    >
                      {t('admin.role.savePermissions')}
                    </button>
                    <ActionStatus state={permissionAction} />
                  </div>
                </>
              )}
            </section>

            {/* Silme */}
            {!role.isSystem && (
              <section className="admin-section admin-danger">
                <h4>{t('admin.role.delete')}</h4>
                {assigned > 0 && (
                  <p className="admin-hint admin-note">{t('admin.role.assignedWarning', { users: role.userCount, groups: role.groupCount })}</p>
                )}
                <label className="login-field">
                  <span>{t('admin.role.deleteHint', { name: role.name })}</span>
                  <input type="text" autoComplete="off" value={deleteConfirm} onChange={e => setDeleteConfirm(e.target.value)} />
                </label>
                <div className="admin-actions">
                  <button
                    type="button"
                    className="cache-btn danger-btn"
                    disabled={deleteAction.busy || deleteConfirm !== role.name}
                    onClick={() => deleteAction.run(async () => { await deleteRole(role.id, assigned > 0); await onDeleted() })}
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