import { useCallback, useEffect, useMemo, useState } from 'react'
import { useI18n } from '../../i18n/useI18n'
import { getPermissions, getRoles } from '../../api'
import type { PermissionResponse, RoleResponse } from '../../types'
import PermissionDialog from './PermissionDialog'
import { Chips } from './AdminUI'

/*
 * Yetkiler sekmesi (permissions.manage). Sistem yetkileri kodun kontrol ettiği anahtarlardır (adı/açıklaması
 * değiştirilebilir, anahtarı değişmez, silinemez); panelden özel yetki eklenebilir. Rollere atama Roller sekmesindedir.
 * Rol adları için rol listesi de yüklenir (GET /api/roles permissions.manage ile okunabilir).
 */
export default function PermissionsTab() {
  const { t, lang } = useI18n()
  const [permissions, setPermissions] = useState<PermissionResponse[]>([])
  const [roles, setRoles] = useState<RoleResponse[]>([])
  const [state, setState] = useState<{ loading: boolean; error?: string }>({ loading: true })
  const [query, setQuery] = useState('')
  // Açık diyalog: 'new' = yeni yetki, sayı = düzenlenen yetkinin id'si
  const [editing, setEditing] = useState<number | 'new' | null>(null)

  const reload = useCallback(async () => {
    try {
      const [p, r] = await Promise.all([getPermissions(), getRoles()])
      setPermissions(p)
      setRoles(r)
      setState({ loading: false })
    } catch (e) {
      setState({ loading: false, error: (e as Error).message })
    }
  }, [])

  useEffect(() => { reload() }, [reload])

  const filtered = useMemo(() => {
    const q = query.trim().toLocaleLowerCase('tr')
    const sorted = [...permissions].sort((a, b) => a.key.localeCompare(b.key))
    if (!q) return sorted
    return sorted.filter(p => [p.key, p.name, p.nameEn, p.description ?? ''].some(s => s.toLocaleLowerCase('tr').includes(q)))
  }, [permissions, query])

  const roleNames = (ids: number[]) => roles.filter(r => ids.includes(r.id)).map(r => r.name)
  const editingPermission = typeof editing === 'number' ? permissions.find(p => p.id === editing) ?? null : null

  return (
    <>
      <p className="admin-hint admin-note admin-intro">{t('admin.permissions.intro')}</p>
      <div className="admin-toolbar">
        <input
          className="search admin-search"
          type="search"
          placeholder={t('admin.permissions.search')}
          aria-label={t('admin.permissions.search')}
          value={query}
          onChange={e => setQuery(e.target.value)}
        />
        <span className="admin-muted">{t('admin.permissions.count', { n: filtered.length })}</span>
        <button type="button" className="cache-btn" onClick={() => setEditing('new')}>{t('admin.permissions.new')}</button>
      </div>

      {state.loading && <p className="admin-placeholder pulse">{t('admin.loading')}</p>}
      {state.error && <p className="note-error" role="alert">{state.error}</p>}
      {!state.loading && !state.error && filtered.length === 0 && <p className="admin-placeholder">{t('admin.permissions.empty')}</p>}

      {!state.loading && !state.error && filtered.length > 0 && (
        <div className="admin-table-wrap">
          <table className="admin-table">
            <thead>
              <tr>
                <th>{t('admin.permissions.col.key')}</th>
                <th>{t('admin.permissions.col.name')}</th>
                <th>{t('admin.permissions.col.description')}</th>
                <th>{t('admin.permissions.col.roles')}</th>
                <th><span className="sr-only">{t('admin.users.col.actions')}</span></th>
              </tr>
            </thead>
            <tbody>
              {filtered.map(p => (
                <tr key={p.id}>
                  <td className="admin-nowrap">
                    <code>{p.key}</code>{' '}
                    <span className="badge badge-off">{t(p.isSystem ? 'admin.system' : 'admin.role.custom')}</span>
                  </td>
                  <td>{lang === 'en' ? p.nameEn : p.name}</td>
                  <td>{p.description || <span className="admin-muted">—</span>}</td>
                  <td><Chips items={roleNames(p.roleIds)} /></td>
                  <td>
                    <button type="button" className="link-btn" onClick={() => setEditing(p.id)}>{t('admin.edit')}</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {(editing === 'new' || editingPermission) && (
        <PermissionDialog
          key={editing}
          permission={editingPermission}
          roleNames={editingPermission ? roleNames(editingPermission.roleIds) : []}
          onChanged={reload}
          onDone={() => { setEditing(null); return reload() }}
          onClose={() => setEditing(null)}
        />
      )}
    </>
  )
}