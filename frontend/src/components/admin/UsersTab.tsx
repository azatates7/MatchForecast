import { useCallback, useEffect, useMemo, useState } from 'react'
import { useI18n } from '../../i18n/useI18n'
import { locale } from '../../i18n/i18n'
import { getGroups, getRoles, getUsers } from '../../api'
import { hasPermission, PERMISSIONS, type GroupResponse, type MeResponse, type RoleResponse, type UserResponse } from '../../types'
import UserDialog from './UserDialog'
import { Chips } from './adminUi'

interface Props {
  me: MeResponse
}

/*
 * Kullanıcılar sekmesi (users.view). Değişiklik düğmeleri yalnızca users.manage yetkisiyle görünür; yetki yoksa
 * diyalog salt okunur açılır. Asıl kontrol API'dedir (kendini pasife alma, son Admin vb. kuralları API mesajıyla gösterilir).
 * Roller ve gruplar diyalogda id ile atanır; liste bu yüzden rol ve grup listelerini de yükler.
 */
export default function UsersTab({ me }: Props) {
  const { t, lang } = useI18n()
  const canManage = hasPermission(me, PERMISSIONS.usersManage)

  const [users, setUsers] = useState<UserResponse[]>([])
  const [roles, setRoles] = useState<RoleResponse[]>([])
  const [groups, setGroups] = useState<GroupResponse[]>([])
  const [state, setState] = useState<{ loading: boolean; error?: string }>({ loading: true })
  const [query, setQuery] = useState('')
  // Açık diyalog: 'new' = yeni kullanıcı, sayı = düzenlenen kullanıcının id'si
  const [editing, setEditing] = useState<number | 'new' | null>(null)

  const reload = useCallback(async () => {
    try {
      const [u, r, g] = await Promise.all([getUsers(), getRoles(), getGroups()])
      setUsers(u)
      setRoles(r)
      setGroups(g)
      setState({ loading: false })
    } catch (e) {
      setState({ loading: false, error: (e as Error).message })
    }
  }, [])

  useEffect(() => { reload() }, [reload])

  const filtered = useMemo(() => {
    const q = query.trim().toLocaleLowerCase('tr')
    if (!q) return users
    return users.filter(u => [u.userName, u.displayName ?? '', u.email].some(s => s.toLocaleLowerCase('tr').includes(q)))
  }, [users, query])

  const formatDate = (iso: string | null) =>
    iso ? new Date(iso).toLocaleString(locale(lang), { dateStyle: 'medium', timeStyle: 'short' }) : '—'

  const editingUser = typeof editing === 'number' ? users.find(u => u.id === editing) ?? null : null

  return (
    <>
      <div className="admin-toolbar">
        <input
          className="search admin-search"
          type="search"
          placeholder={t('admin.users.search')}
          aria-label={t('admin.users.search')}
          value={query}
          onChange={e => setQuery(e.target.value)}
        />
        <span className="admin-muted">{t('admin.users.count', { n: filtered.length })}</span>
        {canManage && (
          <button type="button" className="cache-btn" onClick={() => setEditing('new')}>{t('admin.users.new')}</button>
        )}
      </div>

      {state.loading && <p className="admin-placeholder pulse">{t('admin.loading')}</p>}
      {state.error && <p className="note-error" role="alert">{state.error}</p>}
      {!state.loading && !state.error && filtered.length === 0 && <p className="admin-placeholder">{t('admin.users.empty')}</p>}

      {!state.loading && !state.error && filtered.length > 0 && (
        <div className="admin-table-wrap">
          <table className="admin-table">
            <thead>
              <tr>
                <th>{t('admin.users.col.user')}</th>
                <th>{t('admin.users.col.email')}</th>
                <th>{t('admin.users.col.roles')}</th>
                <th>{t('admin.users.col.groups')}</th>
                <th>{t('admin.users.col.status')}</th>
                <th>{t('admin.users.col.lastLogin')}</th>
                <th><span className="sr-only">{t('admin.users.col.actions')}</span></th>
              </tr>
            </thead>
            <tbody>
              {filtered.map(u => (
                <tr key={u.id}>
                  <td>
                    <strong>{u.userName}</strong>
                    {u.displayName && <div className="admin-sub">{u.displayName}</div>}
                  </td>
                  <td>{u.email}</td>
                  <td><Chips items={u.roles} /></td>
                  <td><Chips items={u.groups} /></td>
                  <td>
                    <span className={u.isActive ? 'badge badge-ok' : 'badge badge-off'}>
                      {t(u.isActive ? 'admin.users.active' : 'admin.users.inactive')}
                    </span>
                  </td>
                  <td className="admin-nowrap">{formatDate(u.lastLoginAt)}</td>
                  <td>
                    <button type="button" className="link-btn" onClick={() => setEditing(u.id)}>
                      {t(canManage ? 'admin.edit' : 'admin.view')}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {(editing === 'new' || editingUser) && (
        <UserDialog
          key={editing}
          me={me}
          user={editingUser}
          roles={roles}
          groups={groups}
          canManage={canManage}
          onChanged={reload}
          onCreated={async id => { await reload(); setEditing(id) }}
          onDeleted={() => { setEditing(null); return reload() }}
          onClose={() => setEditing(null)}
        />
      )}
    </>
  )
}