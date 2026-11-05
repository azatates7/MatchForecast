import { useCallback, useEffect, useState } from 'react'
import { useI18n } from '../../i18n/useI18n'
import { getGroups, getRoles, getUsers } from '../../api'
import type { GroupResponse, MeResponse, RoleResponse, UserResponse } from '../../types'
import GroupDialog from '../admin/GroupDialog'
import { Chips } from './adminUi'

interface Props {
  me: MeResponse
}

/*
 * Gruplar sekmesi (groups.manage). Gruba verilen roller her üyeye geçer. Rol ve kullanıcı listeleri de yüklenir
 * (GET /api/roles ve /api/users groups.manage ile okunabilir). Admin rolü veren gruplarda silme / rol kaldırma /
 * üye çıkarma kuralları API'dedir (kendi Admin'liğini kaldırma, son Admin); mesajı diyalogda gösterilir.
 */
export default function GroupsTab({ me }: Props) {
  const { t } = useI18n()
  const [groups, setGroups] = useState<GroupResponse[]>([])
  const [roles, setRoles] = useState<RoleResponse[]>([])
  const [users, setUsers] = useState<UserResponse[]>([])
  const [state, setState] = useState<{ loading: boolean; error?: string }>({ loading: true })
  // Açık diyalog: 'new' = yeni grup, sayı = düzenlenen grubun id'si
  const [editing, setEditing] = useState<number | 'new' | null>(null)

  const reload = useCallback(async () => {
    try {
      const [g, r, u] = await Promise.all([getGroups(), getRoles(), getUsers()])
      setGroups(g)
      setRoles(r)
      setUsers(u)
      setState({ loading: false })
    } catch (e) {
      setState({ loading: false, error: (e as Error).message })
    }
  }, [])

  useEffect(() => { reload() }, [reload])

  const roleNames = (ids: number[]) => roles.filter(r => ids.includes(r.id)).map(r => r.name)
  const editingGroup = typeof editing === 'number' ? groups.find(g => g.id === editing) ?? null : null

  return (
    <>
      <div className="admin-toolbar">
        <span className="admin-muted admin-grow">{t('admin.groups.count', { n: groups.length })}</span>
        <button type="button" className="cache-btn" onClick={() => setEditing('new')}>{t('admin.groups.new')}</button>
      </div>

      {state.loading && <p className="admin-placeholder pulse">{t('admin.loading')}</p>}
      {state.error && <p className="note-error" role="alert">{state.error}</p>}
      {!state.loading && !state.error && groups.length === 0 && <p className="admin-placeholder">{t('admin.groups.empty')}</p>}

      {!state.loading && !state.error && groups.length > 0 && (
        <div className="admin-table-wrap">
          <table className="admin-table">
            <thead>
              <tr>
                <th>{t('admin.groups.col.name')}</th>
                <th>{t('admin.groups.col.description')}</th>
                <th>{t('admin.groups.col.roles')}</th>
                <th>{t('admin.groups.col.members')}</th>
                <th><span className="sr-only">{t('admin.users.col.actions')}</span></th>
              </tr>
            </thead>
            <tbody>
              {groups.map(g => (
                <tr key={g.id}>
                  <td><strong>{g.name}</strong></td>
                  <td>{g.description || <span className="admin-muted">—</span>}</td>
                  <td><Chips items={roleNames(g.roleIds)} /></td>
                  <td>{g.memberIds.length}</td>
                  <td>
                    <button type="button" className="link-btn" onClick={() => setEditing(g.id)}>{t('admin.edit')}</button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {(editing === 'new' || editingGroup) && (
        <GroupDialog
          key={editing}
          me={me}
          group={editingGroup}
          roles={roles}
          users={users}
          onChanged={reload}
          onCreated={async id => { await reload(); setEditing(id) }}
          onDeleted={() => { setEditing(null); return reload() }}
          onClose={() => setEditing(null)}
        />
      )}
    </>
  )
}