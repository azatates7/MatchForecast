import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useI18n } from '../../i18n/useI18n'
import { createUser, deleteUser, generatePassword, setUserGroups, setUserPassword, setUserRoles, updateUser } from '../../api'
import type { GroupResponse, MeResponse, RoleResponse, UserResponse } from '../../types'
import { ActionStatus, Chips, useAction } from './AdminUI'

interface Props {
  me: MeResponse
  /** null = yeni kullanıcı */
  user: UserResponse | null
  roles: RoleResponse[]
  groups: GroupResponse[]
  canManage: boolean
  onChanged: () => Promise<void>
  onCreated: (id: number) => Promise<void>
  onDeleted: () => Promise<void>
  onClose: () => void
}

const idsOf = <T extends { id: number; name: string }>(all: T[], names: string[]) =>
  all.filter(x => names.includes(x.name)).map(x => x.id)

/*
 * Kullanıcı diyaloğu (native <dialog>; Esc ile kapanır). Yeni kullanıcıda yalnızca oluşturma formu vardır; oluşturulunca
 * aynı diyalog düzenleme moduna geçer ki rol ve grup hemen atanabilsin. Düzenlemede bölümler ayrı ayrı kaydedilir:
 * profil, roller, gruplar, şifre, silme. users.manage yoksa her şey salt okunurdur (fieldset disabled, düğme yok).
 */
export default function UserDialog({ me, user, roles, groups, canManage, onChanged, onCreated, onDeleted, onClose }: Props) {
  const { t } = useI18n()
  const dialog = useRef<HTMLDialogElement>(null)
  useEffect(() => { dialog.current?.showModal() }, [])

  const isSelf = !!user && user.userName.toLowerCase() === me.userName.toLowerCase()
  const isGuest = !!user && user.roles.includes('Guest')

  // Profil / yeni kullanıcı formu
  const [userName, setUserName] = useState('')
  const [email, setEmail] = useState(user?.email ?? '')
  const [displayName, setDisplayName] = useState(user?.displayName ?? '')
  const [isActive, setIsActive] = useState(user?.isActive ?? true)
  const [password, setPassword] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [generating, setGenerating] = useState(false)

  const [roleIds, setRoleIds] = useState<number[]>(() => idsOf(roles, user?.roles ?? []))
  const [groupIds, setGroupIds] = useState<number[]>(() => idsOf(groups, user?.groups ?? []))
  const [deleteConfirm, setDeleteConfirm] = useState('')

  const profile = useAction()
  const roleAction = useAction()
  const groupAction = useAction()
  const passwordAction = useAction()
  const deleteAction = useAction()

  const toggle = (list: number[], id: number) => (list.includes(id) ? list.filter(x => x !== id) : [...list, id])

  const handleGenerate = async () => {
    setGenerating(true)
    try {
      setPassword((await generatePassword(16)).password)
      setShowPassword(true)
    } catch { /* üretilemezse kullanıcı elle yazar */ }
    setGenerating(false)
  }

  const submitCreate = (e: FormEvent) => {
    e.preventDefault()
    profile.run(async () => {
      const created = await createUser({ userName: userName.trim(), email: email.trim(), displayName: displayName.trim() || null, password })
      await onCreated(created.id)
    })
  }

  const submitProfile = (e: FormEvent) => {
    e.preventDefault()
    if (!user) return
    profile.run(async () => {
      await updateUser(user.id, { email: email.trim(), displayName: displayName.trim() || null, isActive })
      await onChanged()
    }, t('admin.saved'))
  }

  const submitPassword = (e: FormEvent) => {
    e.preventDefault()
    if (!user) return
    passwordAction.run(async () => {
      await setUserPassword(user.id, password)
      setPassword('')
    }, t('admin.user.passwordSet'))
  }

  const passwordTools = (
    <div className="password-tools">
      <button type="button" className="link-btn" onClick={handleGenerate} disabled={generating}>
        {t(generating ? 'auth.generating' : 'auth.generate')}
      </button>
      {password && (
        <button type="button" className="link-btn" onClick={() => setShowPassword(v => !v)}>
          {t(showPassword ? 'auth.hide' : 'auth.show')}
        </button>
      )}
    </div>
  )

  return (
    <dialog ref={dialog} className="admin-dialog" onClose={onClose} aria-labelledby="user-dialog-title">
      <div className="admin-dialog-head">
        <h3 id="user-dialog-title">{user ? user.userName : t('admin.users.new')}</h3>
        <button type="button" className="cache-btn" onClick={() => dialog.current?.close()}>{t('admin.close')}</button>
      </div>

      <div className="admin-dialog-body">
        {!user ? (
          <form className="admin-section" onSubmit={submitCreate}>
            <div className="admin-form-grid">
              <label className="login-field">
                <span>{t('auth.userName')}</span>
                <input type="text" required minLength={3} maxLength={50} autoComplete="off" value={userName} onChange={e => setUserName(e.target.value)} />
              </label>
              <label className="login-field">
                <span>{t('auth.email')}</span>
                <input type="email" required maxLength={256} autoComplete="off" value={email} onChange={e => setEmail(e.target.value)} />
              </label>
              <label className="login-field">
                <span>{t('auth.displayName')}</span>
                <input type="text" maxLength={100} autoComplete="off" value={displayName} onChange={e => setDisplayName(e.target.value)} />
              </label>
              <label className="login-field">
                <span>{t('auth.password')}</span>
                <input type={showPassword ? 'text' : 'password'} required minLength={8} maxLength={128} autoComplete="new-password" value={password} onChange={e => setPassword(e.target.value)} />
              </label>
            </div>
            {passwordTools}
            <p className="admin-hint">{t('admin.user.createHint')}</p>
            <div className="admin-actions">
              <button type="submit" className="cache-btn" disabled={profile.busy}>{t('admin.user.create')}</button>
              <ActionStatus state={profile} />
            </div>
          </form>
        ) : (
          <>
            {isSelf && <p className="admin-hint admin-note">{t('admin.user.self')}</p>}
            {isGuest && <p className="admin-hint admin-note">{t('admin.user.guest')}</p>}

            {/* Profil */}
            <form className="admin-section" onSubmit={submitProfile}>
              <h4>{t('admin.user.profile')}</h4>
              <fieldset className="admin-fieldset" disabled={!canManage}>
                <div className="admin-form-grid">
                  <label className="login-field">
                    <span>{t('auth.email')}</span>
                    <input type="email" required maxLength={256} value={email} onChange={e => setEmail(e.target.value)} />
                  </label>
                  <label className="login-field">
                    <span>{t('auth.displayName')}</span>
                    <input type="text" maxLength={100} value={displayName} onChange={e => setDisplayName(e.target.value)} />
                  </label>
                </div>
                <label className="admin-check">
                  <input type="checkbox" checked={isActive} disabled={isSelf} onChange={e => setIsActive(e.target.checked)} />
                  <span>{t('admin.user.isActive')}</span>
                </label>
              </fieldset>
              {canManage && (
                <div className="admin-actions">
                  <button type="submit" className="cache-btn" disabled={profile.busy}>{t('admin.save')}</button>
                  <ActionStatus state={profile} />
                </div>
              )}
            </form>

            {/* Roller */}
            <section className="admin-section">
              <h4>{t('admin.user.roles')}</h4>
              <fieldset className="admin-fieldset admin-check-list" disabled={!canManage}>
                {roles.map(r => (
                  <label key={r.id} className="admin-check">
                    <input type="checkbox" checked={roleIds.includes(r.id)} onChange={() => setRoleIds(ids => toggle(ids, r.id))} />
                    <span>{r.name}</span>
                    {r.isSystem && <span className="badge badge-off">{t('admin.system')}</span>}
                  </label>
                ))}
              </fieldset>
              {canManage && (
                <div className="admin-actions">
                  <button
                    type="button"
                    className="cache-btn"
                    disabled={roleAction.busy}
                    onClick={() => roleAction.run(async () => { await setUserRoles(user.id, roleIds); await onChanged() }, t('admin.saved'))}
                  >
                    {t('admin.user.saveRoles')}
                  </button>
                  <ActionStatus state={roleAction} />
                </div>
              )}
            </section>

            {/* Gruplar */}
            <section className="admin-section">
              <h4>{t('admin.user.groups')}</h4>
              {groups.length === 0 ? (
                <p className="admin-hint">{t('admin.user.noGroups')}</p>
              ) : (
                <>
                  <fieldset className="admin-fieldset admin-check-list" disabled={!canManage}>
                    {groups.map(g => (
                      <label key={g.id} className="admin-check">
                        <input type="checkbox" checked={groupIds.includes(g.id)} onChange={() => setGroupIds(ids => toggle(ids, g.id))} />
                        <span>{g.name}</span>
                      </label>
                    ))}
                  </fieldset>
                  {canManage && (
                    <div className="admin-actions">
                      <button
                        type="button"
                        className="cache-btn"
                        disabled={groupAction.busy}
                        onClick={() => groupAction.run(async () => { await setUserGroups(user.id, groupIds); await onChanged() }, t('admin.saved'))}
                      >
                        {t('admin.user.saveGroups')}
                      </button>
                      <ActionStatus state={groupAction} />
                    </div>
                  )}
                </>
              )}
            </section>

            {/* Etkin yetkiler (salt okunur) */}
            <section className="admin-section">
              <h4>{t('admin.user.permissions')}</h4>
              {user.permissions.length === 0 ? <p className="admin-hint">{t('admin.user.noPermissions')}</p> : <Chips items={user.permissions} />}
              <p className="admin-hint">{t('admin.user.permissionsHint')}</p>
            </section>

            {canManage && (
              <>
                {/* Şifre */}
                <form className="admin-section" onSubmit={submitPassword}>
                  <h4>{t('admin.user.passwordTitle')}</h4>
                  <label className="login-field">
                    <span>{t('auth.newPassword')}</span>
                    <input type={showPassword ? 'text' : 'password'} required minLength={8} maxLength={128} autoComplete="new-password" value={password} onChange={e => setPassword(e.target.value)} />
                  </label>
                  {passwordTools}
                  <p className="admin-hint">{t('admin.user.passwordHint')}</p>
                  <div className="admin-actions">
                    <button type="submit" className="cache-btn" disabled={passwordAction.busy}>{t('admin.user.setPassword')}</button>
                    <ActionStatus state={passwordAction} />
                  </div>
                </form>

                {/* Silme */}
                {!isSelf && (
                  <section className="admin-section admin-danger">
                    <h4>{t('admin.user.delete')}</h4>
                    <label className="login-field">
                      <span>{t('admin.user.deleteHint', { name: user.userName })}</span>
                      <input type="text" autoComplete="off" value={deleteConfirm} onChange={e => setDeleteConfirm(e.target.value)} />
                    </label>
                    <div className="admin-actions">
                      <button
                        type="button"
                        className="cache-btn danger-btn"
                        disabled={deleteAction.busy || deleteConfirm !== user.userName}
                        onClick={() => deleteAction.run(async () => { await deleteUser(user.id); await onDeleted() })}
                      >
                        {t('admin.user.deleteConfirm')}
                      </button>
                      <ActionStatus state={deleteAction} />
                    </div>
                  </section>
                )}
              </>
            )}
          </>
        )}
      </div>
    </dialog>
  )
}