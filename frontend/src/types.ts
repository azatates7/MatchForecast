import type { LocalizedText } from './i18n/i18n'

export type { LocalizedText }

export interface MatchSummary {
  id: number
  kickoff: string
  league: string
  country: string
  homeTeam: string
  awayTeam: string
  status: string
  /** Devam eden maçta oynanan dakika; başlamamış maçta null */
  elapsed?: number | null
  /** Uzatma dakikası (45+2 için 2); yoksa null */
  elapsedExtra?: number | null
}

export interface Prediction {
  rank: number
  /** İki dilli etiket, ör. { tr: "KG Var", en: "BTTS Yes" } */
  label: LocalizedText
  market: string
  selection: string
  /** Hesaplanan gerçekleşme olasılığı (0-100) */
  confidence: number
  reasoning: LocalizedText
}

export type StatMetric =
  | 'HomeGoals' | 'AwayGoals' | 'TotalGoals' | 'FirstHalfGoals'
  | 'Corners' | 'YellowCards' | 'RedCards'

/** Bir Alt/Üst çizgisi; over + under = 100 */
export interface LineProbability {
  line: number
  over: number
  under: number
}

export interface StatProjection {
  metric: StatMetric
  label: LocalizedText
  /** Beklenen değer (ör. 2.6 gol) */
  expected: number
  /** Olası aralık: dağılımın ortadaki %80'i */
  rangeMin: number
  rangeMax: number
  /** Korner için her zaman boş */
  lines: LineProbability[]
  reasoning: LocalizedText
}

/** Yüzde; home + draw + away = 100. İlk yarı alanları model ilk yarı beklentisi vermediyse null */
export interface OutcomeProbabilities {
  home: number
  draw: number
  away: number
  bttsYes: number
  firstHalfHome: number | null
  firstHalfDraw: number | null
  firstHalfAway: number | null
}

export interface ForecastResult {
  match: MatchSummary
  summary: LocalizedText
  outcomes: OutcomeProbabilities
  stats: StatProjection[]
  predictions: Prediction[]
  generatedAt: string
}

export interface AuthConfigResponse {
  googleClientId: string | null
  guestLoginEnabled: boolean
}

export interface TokenResponse {
  accessToken: string
  tokenType: string
  expiresAt: string
  refreshToken: string
  refreshTokenExpiresAt: string
}

// Giriş yapmış kullanıcı (üst bardaki ad ve misafir kısıtları için)
export interface MeResponse {
  userName: string
  displayName: string | null
  roles: string[]
  /** Etkin yetki anahtarları (ör. "cache.clear"); token yenilenince güncellenir */
  permissions: string[]
}

/** API ile aynı kural: Admin her yetkiye sahiptir. Yalnızca görünüm içindir; asıl kontrol API'dedir. */
export const hasPermission = (me: MeResponse | null, key: string): boolean =>
  !!me && (me.roles.includes('Admin') || (me.permissions ?? []).includes(key))

/** Sistem yetki anahtarları (backend: PermissionKeys) */
export const PERMISSIONS = {
  usersView: 'users.view',
  usersManage: 'users.manage',
  rolesManage: 'roles.manage',
  permissionsManage: 'permissions.manage',
  groupsManage: 'groups.manage',
  cacheClear: 'cache.clear',
  forecastRefresh: 'forecast.refresh',
  appSettingsManage: 'app-settings.manage'
} as const

// ---- Admin paneli (kullanıcı / rol / grup / yetki yönetimi) ----

export interface UserResponse {
  id: number
  userName: string
  email: string
  displayName: string | null
  isActive: boolean
  emailConfirmed: boolean
  createdAt: string
  updatedAt: string | null
  lastLoginAt: string | null
  /** Doğrudan atanmış rol adları */
  roles: string[]
  /** Üyesi olduğu grup adları */
  groups: string[]
  /** Etkin yetkiler (doğrudan + grup rolleri; Admin → tümü), salt okunur */
  permissions: string[]
}

export interface CreateUserRequest {
  userName: string
  email: string
  displayName: string | null
  password: string
}

export interface UpdateUserRequest {
  email: string
  displayName: string | null
  isActive: boolean
}

export interface RoleResponse {
  id: number
  name: string
  description: string | null
  /** Admin, User, Guest: adı değiştirilemez, silinemez */
  isSystem: boolean
  /** Admin: tüm yetkilere örtük olarak sahip; permissionIds boş ve düzenlenemez */
  hasAllPermissions: boolean
  permissionIds: number[]
  userCount: number
  groupCount: number
}

export interface RoleRequest {
  name: string
  description: string | null
}

export interface GroupResponse {
  id: number
  name: string
  description: string | null
  roleIds: number[]
  memberIds: number[]
}

export interface GroupRequest {
  name: string
  description: string | null
}

export interface PermissionResponse {
  id: number
  /** "alan.işlem" (ör. users.view); JWT'deki perm claim'i */
  key: string
  name: string
  nameEn: string
  description: string | null
  /** Kodun kontrol ettiği yetki: anahtarı değişmez, silinemez */
  isSystem: boolean
  roleIds: number[]
}

export interface CreatePermissionRequest {
  key: string
  name: string
  nameEn: string
  description: string | null
}

/** key null ise anahtar değişmez (sistem yetkilerinde değiştirilemez) */
export interface UpdatePermissionRequest {
  key: string | null
  name: string
  nameEn: string
  description: string | null
}

export interface CacheClearResponse {
  deletedKeys: number
}

export interface PasswordResponse {
  password: string
  length: number
}