import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { useI18n } from '../../i18n/useI18n'
import { createGroup, deleteGroup, setGroupRoles, setGroupUsers, updateGroup } from '../../api'
import type { GroupResponse, MeResponse, RoleResponse, UserResponse } from '../../types'
import { ActionStatus, useAction } from './AdminUI'

interface Props {
  me: MeResponse
  /** null = yeni grup */
  group: GroupResponse | null
  roles: RoleResponse[]
  users: UserResponse[]
  onChanged: () => Promise<void>
  onCreated: (id: number) => Promise<void>
  onDeleted: () => Promise<void>
  onClose: () => void
}

/*
 * Grup diyaloğu (native <dialog>). Yeni grupta yalnızca ad/açıklama; oluşturulunca aynı diyalog düzenleme moduna geçer.
 * Düzenlemede: bilgiler, roller (Admin seçiliyse üyelerin Admin olacağı uyarısı), üyeler (aranabilir liste), silme.
 * Kullanıcıların kendisi silinmez; yalnızca üyelikler ve grubun rol atamaları kaldırılır.
 */
export default function GroupDialog({ me, group, roles, users, onChanged, onCreated, onDeleted, onClose }: Props) {
  const { t } = useI18n()
  const dialog = useRef<HTMLDialogElement>(null)
  useEffect(() => { dialog.current?.showModal() }, [])

  const [name, setName] = useState(group?.name ?? '')
  const [description, setDescription] = useState(group?.description ?? '')
  const [roleIds, setRoleIds] = useState<number[]>(group?.roleIds ?? [])
  const [memberIds, setMemberIds] = useState<number[]>(group?.memberIds ?? [])
  const [memberQuery, setMemberQuery] = useState('')
  const [deleteConfirm, setDeleteConfirm] = useState('')

  const info = useAction()
  const roleAction = useAction()
  const memberAction = useAction()
  const deleteAction = useAction()

  const toggle = (list: number[], id: number) => (list.includes(id) ? list.filter(x => x !== id) : [...list, id])
  const grantsAdmin = roles.some(r => r.hasAllPermissions && roleIds.includes(r.id))

  // Üye listesi: aramaya uyanlar, önce seçili üyeler, sonra kullanıcı adına göre.
  const memberOptions = useMemo(() => {
    const q = memberQuery.trim().toLocaleLowerCase('tr')
    const matches = (u: UserResponse) => !q || [u.userName, u.displayName ?? ''].some(s => s.toLocaleLowerCase('tr').includes(q))
    const selected = (u: UserResponse) => (group?.memberIds.includes(u.id) ? 0 : 1)
    return users.filter(matches).sort((a, b) => selected(a) - selected(b) || a.userName.localeCompare(b.userName))
  }, [users, memberQuery, group])

  const submitInfo = (e: FormEvent) => {
    e.preventDefault()
    const body = { name: name.trim(), description: description.trim() || null }
    if (!group) {
      info.run(async () => { const created = await createGroup(body); await onCreated(created.id) })
      return
    }
    info.run(async () => { await updateGroup(group.id, body); await onChanged() }, t('admin.saved'))
  }

  return (
    <dialog ref={dialog} className="admin-dialog" onClose={onClose} aria-labelledby="group-dialog-title">
      <div className="admin-dialog-head">
        <h3 id="group-dialog-title">{group ? group.name : t('admin.groups.new')}</h3>
        <button type="button" className="cache-btn" onClick={() => dialog.current?.close()}>{t('admin.close')}</button>
      </div>

      <div className="admin-dialog-body">
        {/* Bilgiler */}
        <form className="admin-section" onSubmit={submitInfo}>
          {group && <h4>{t('admin.group.info')}</h4>}
          <div className="admin-form-grid">
            <label className="login-field">
              <span>{t('admin.group.name')}</span>
              <input type="text" required maxLength={100} autoComplete="off" value={name} onChange={e => setName(e.target.value)} />
            </label>
            <label className="login-field">
              <span>{t('admin.group.description')}</span>
              <input type="text" maxLength={250} autoComplete="off" value={description} onChange={e => setDescription(e.target.value)} />
            </label>
          </div>
          {!group && <p className="admin-hint">{t('admin.group.createHint')}</p>}
          <div className="admin-actions">
            <button type="submit" className="cache-btn" disabled={info.busy}>{t(group ? 'admin.save' : 'admin.create')}</button>
            <ActionStatus state={info} />
          </div>
        </form>

        {group && (
          <>
            {/* Roller */}
            <section className="admin-section">
              <h4>{t('admin.group.roles')}</h4>
              <div className="admin-fieldset admin-check-list">
                {roles.map(r => (
                  <label key={r.id} className="admin-check">
                    <input type="checkbox" checked={roleIds.includes(r.id)} onChange={() => setRoleIds(ids => toggle(ids, r.id))} />
                    <span>{r.name}</span>
                    {r.isSystem && <span className="badge badge-off">{t('admin.system')}</span>}
                  </label>
                ))}
              </div>
              {grantsAdmin && <p className="admin-hint admin-note">{t('admin.group.adminWarning')}</p>}
              <p className="admin-hint">{t('admin.group.rolesHint')}</p>
              <div className="admin-actions">
                <button
                  type="button"
                  className="cache-btn"
                  disabled={roleAction.busy}
                  onClick={() => roleAction.run(async () => { await setGroupRoles(group.id, roleIds); await onChanged() }, t('admin.saved'))}
                >
                  {t('admin.group.saveRoles')}
                </button>
                <ActionStatus state={roleAction} />
              </div>
            </section>

            {/* Üyeler */}
            <section className="admin-section">
              <h4>{t('admin.group.members')}</h4>
              {users.length === 0 ? (
                <p className="admin-hint">{t('admin.group.noUsers')}</p>
              ) : (
                <>
                  <div className="admin-toolbar admin-toolbar-tight">
                    <input
                      className="search admin-search"
                      type="search"
                      placeholder={t('admin.group.membersSearch')}
                      aria-label={t('admin.group.membersSearch')}
                      value={memberQuery}
                      onChange={e => setMemberQuery(e.target.value)}
                    />
                    <span className="admin-muted">{t('admin.group.membersSelected', { n: memberIds.length })}</span>
                  </div>
                  <div className="admin-member-list">
                    {memberOptions.map(u => (
                      <label key={u.id} className="admin-check">
                        <input type="checkbox" checked={memberIds.includes(u.id)} onChange={() => setMemberIds(ids => toggle(ids, u.id))} />
                        <span>
                          {u.userName}
                          {u.displayName && <span className="admin-muted"> · {u.displayName}</span>}
                          {u.userName.toLowerCase() === me.userName.toLowerCase() && <span className="badge badge-off">{t('admin.group.you')}</span>}
                          {!u.isActive && <span className="badge badge-off">{t('admin.users.inactive')}</span>}
                        </span>
                      </label>
                    ))}
                  </div>
                  <div className="admin-actions">
                    <button
                      type="button"
                      className="cache-btn"
                      disabled={memberAction.busy}
                      onClick={() => memberAction.run(async () => { await setGroupUsers(group.id, memberIds); await onChanged() }, t('admin.saved'))}
                    >
                      {t('admin.group.saveMembers')}
                    </button>
                    <ActionStatus state={memberAction} />
                  </div>
                </>
              )}
            </section>

            {/* Silme */}
            <section className="admin-section admin-danger">
              <h4>{t('admin.group.delete')}</h4>
              {group.memberIds.length > 0 && (
                <p className="admin-hint admin-note">{t('admin.group.deleteWarning', { n: group.memberIds.length })}</p>
              )}
              <label className="login-field">
                <span>{t('admin.group.deleteHint', { name: group.name })}</span>
                <input type="text" autoComplete="off" value={deleteConfirm} onChange={e => setDeleteConfirm(e.target.value)} />
              </label>
              <div className="admin-actions">
                <button
                  type="button"
                  className="cache-btn danger-btn"
                  disabled={deleteAction.busy || deleteConfirm !== group.name}
                  onClick={() => deleteAction.run(async () => { await deleteGroup(group.id); await onDeleted() })}
                >
                  {t('admin.user.deleteConfirm')}
                </button>
                <ActionStatus state={deleteAction} />
              </div>
            </section>
          </>
        )}
      </div>
    </dialog>
  )
}