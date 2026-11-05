import { useCallback, useEffect, useState } from 'react'
import { useI18n } from '../../i18n/useI18n'
import { getPermissions, getRoles } from '../../api'
import type { PermissionResponse, RoleResponse } from '../../types'
import RoleDialog from '../admin/RoleDialog'

/*
 * Roller sekmesi (roles.manage). Rol oluşturma, yeniden adlandırma, silme ve role yetki atama burada yapılır.
 * Yetki listesi de yüklenir (GET /api/permissions roles.manage ile okunabilir). Sistem rolleri (Admin, User, Guest)
 * yeniden adlandırılamaz/silinemez; Admin tüm yetkilere sahiptir ve yetki listesi düzenlenemez. Kurallar API'dedir.
 */
export default function RolesTab() {
  const { t } = useI18n()
  const [roles, setRoles] = useState<RoleResponse[]>([])
  const [permissions, setPermissions] = useState<PermissionResponse[]>([])
  const [state, setState] = useState<{ loading: boolean; error?: string }>({ loading: true })
  // Açık diyalog: 'new' = yeni rol, sayı = düzenlenen rolün id'si
  const [editing, setEditing] = useState<number | 'new' | null>(null)

  const reload = useCallback(async () => {
    try {
      const [r, p] = await Promise.all([getRoles(), getPermissions()])
      setRoles(r)
      setPermissions(p)
      setState({ loading: false })
    } catch (e) {
      setState({ loading: false, error: (e as Error).message })
    }
  }, [])

  useEffect(() => { reload() }, [reload])

  const editingRole = typeof editing === 'number' ? roles.find(r => r.id === editing) ?? null : null

  return (
    <>
      <div className="admin-toolbar">
        <span className="admin-muted admin-grow">{t('admin.roles.count', { n: roles.length })}</span>
        <button type="button" className="cache-btn" onClick={() => setEditing('new')}>{t('admin.roles.new')}</button>
      </div>

      {state.loading && <p className="admin-placeholder pulse">{t('admin.loading')}</p>}
      {state.error && <p className="note-error" role="alert">{state.error}</p>}

      {!state.loading && !state.error && (
        <div className="admin-table-wrap">
          <table className="admin-table">
            <thead>
              <tr>
                <th>{t('admin.roles.col.name')}</th>
                <th>{t('admin.roles.col.description')}</th>
                <th>{t('admin.roles.col.permissions')}</th>
                <th>{t('admin.roles.col.users')}</th>
                <th>{t('admin.roles.col.groups')}</th>
                <th><span className="sr-only">{t('admin.users.col.actions')}</span></th>
              </tr>
            </thead>
            <tbody>
              {roles.map(r => (
                <tr key={r.id}>
                  <td>
                    <strong>{r.name}</strong>{' '}
                    {r.isSystem && <span className="badge badge-off">{t('admin.system')}</span>}
                  </td>
                  <td>{r.description || <span className="admin-muted">—</span>}</td>
                  <td className="admin-nowrap">
                    {r.hasAllPermissions ? t('admin.roles.allPermissions') : t('admin.roles.permissionCount', { n: r.permissionIds.length })}
                  </td>
                  <td>{r.userCount}</td>
                  <td>{r.groupCount}</td>
                  <td>
                    <button type="button" className="link-btn" onClick={() => setEditing(r.id)}>{t('admin.edit')}</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {(editing === 'new' || editingRole) && (
        <RoleDialog
          key={editing}
          role={editingRole}
          permissions={permissions}
          onChanged={reload}
          onCreated={async id => { await reload(); setEditing(id) }}
          onDeleted={() => { setEditing(null); return reload() }}
          onClose={() => setEditing(null)}
        />
      )}
    </>
  )
}