import type { MessageKey } from './i18n/messages'
import { hasPermission, PERMISSIONS, type MeResponse } from './types'

/*
 * Yönetim görünümü (adres çubuğunda #admin veya #admin/<sekme>). Router kullanılmaz; App hashchange'i dinler.
 * Her sekme yalnızca ilgili yetkisi olana görünür (Admin hepsini görür). Hiç sekmesi olmayan biri #admin açarsa
 * maç ekranında kalır. Asıl kontrol API'dedir; bu yalnızca görünüm içindir.
 * Not: frontend-angular/src/app/core/admin-view.ts ile bu dosya birbirinin kopyasıdır, birlikte güncelleyin.
 */
export type AdminTab = 'users' | 'roles' | 'groups' | 'permissions'

export interface AdminTabDef {
  id: AdminTab
  permission: string
  label: MessageKey
}

export const ADMIN_TABS: readonly AdminTabDef[] = [
  { id: 'users', permission: PERMISSIONS.usersView, label: 'admin.tab.users' },
  { id: 'roles', permission: PERMISSIONS.rolesManage, label: 'admin.tab.roles' },
  { id: 'groups', permission: PERMISSIONS.groupsManage, label: 'admin.tab.groups' },
  { id: 'permissions', permission: PERMISSIONS.permissionsManage, label: 'admin.tab.permissions' }
]

export const adminHash = (tab?: AdminTab) => (tab ? `#admin/${tab}` : '#admin')

// "#admin" → sekme yok (ilk görünen sekme açılır); "#admin/roles" → roles; başka bir şey → yönetim görünümü değil.
export const parseAdminHash = (hash: string): { admin: boolean; tab: AdminTab | null } => {
  const match = /^#admin(?:\/([a-z]+))?$/.exec(hash)
  if (!match) return { admin: false, tab: null }
  const tab = ADMIN_TABS.find(x => x.id === match[1])?.id ?? null
  return { admin: true, tab }
}

export const visibleAdminTabs = (me: MeResponse | null) => ADMIN_TABS.filter(x => hasPermission(me, x.permission))

export const canOpenAdmin = (me: MeResponse | null) => visibleAdminTabs(me).length > 0

// Hash'i adres çubuğundan "#" bırakmadan kaldırır (maç ekranına dönüş, çıkış). hashchange tetiklenmez.
export const clearHash = () => history.pushState(null, '', location.pathname + location.search)