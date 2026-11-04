import type {
  AuthConfigResponse, CacheClearResponse, CreatePermissionRequest, CreateUserRequest, ForecastResult, GroupRequest, GroupResponse,
  MatchSummary, MeResponse, PasswordResponse, PermissionResponse, RoleRequest, RoleResponse, TokenResponse, UpdatePermissionRequest,
  UpdateUserRequest, UserResponse
} from './types'
import { getLang, t } from './i18n/i18n'

/*
 * Oturum:
 * - Access token yalnızca bellekte tutulur (kısa ömürlü; XSS ile kalıcı olarak ele geçirilemez).
 * - Refresh token localStorage'da tutulur ki sayfa yenilenince tekrar giriş gerekmesin. Bedeli: XSS olursa okunabilir.
 *   Daha güvenli seçenek httpOnly cookie'dir (backend değişikliği gerekir).
 * - Access token yoksa veya 401 alınırsa refresh token ile yenisi alınır; o da başarısız olursa oturum kapanır
 *   ve onSessionExpired ile uygulamaya haber verilir (login ekranı gösterilir).
 */
const REFRESH_KEY = 'matchforecast.refreshToken'

// Her istekte seçili dil gönderilir; API hata mesajlarını (ProblemDetails.detail) bu dilde döner.
const apiFetch = (url: string, init: RequestInit = {}) =>
  fetch(url, { ...init, headers: { ...(init.headers as Record<string, string> | undefined), 'Accept-Language': getLang() } })

let accessToken: string | null = null
// Aynı anda 401 alan istekler TEK bir refresh çağrısını paylaşır. Refresh token tek kullanımlık olduğu için iki kez
// gönderilirse backend bunu "çalınmış token" sayar ve kullanıcının tüm oturumlarını kapatır.
let refreshing: Promise<string | null> | null = null
let sessionExpiredHandler: (() => void) | null = null

export const onSessionExpired = (handler: () => void) => { sessionExpiredHandler = handler }

// localStorage gizli pencerede/engellendiğinde hata fırlatabilir; bu durumda oturum yalnızca sayfa açıkken sürer.
const readRefreshToken = (): string | null => {
  try { return localStorage.getItem(REFRESH_KEY) } catch { return null }
}

const saveSession = (t: TokenResponse) => {
  accessToken = t.accessToken
  try { localStorage.setItem(REFRESH_KEY, t.refreshToken) } catch { /* yalnızca bellekte kalır */ }
}

const clearSession = () => {
  accessToken = null
  try { localStorage.removeItem(REFRESH_KEY) } catch { /* yok say */ }
}

export const hasSession = () => accessToken !== null || readRefreshToken() !== null

async function readJson<T>(res: Response): Promise<T> {
  if (!res.ok) {
    // API ProblemDetails döner; "detail" alanı kullanıcıya gösterilebilir mesajdır.
    const problem = await res.json().catch(() => null)
    // Model doğrulama hataları (400) "detail" yerine "errors" alanında gelir; ilk mesaj gösterilir.
    const validation = problem?.errors ? Object.values(problem.errors as Record<string, string[]>).flat()[0] : undefined
    throw new Error(problem?.detail ?? validation ?? t('api.failed', { status: res.status }))
  }
  // 204 No Content (PUT/DELETE) gövde içermez
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}

const postJson = (url: string, body: unknown) =>
  apiFetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })

export async function login(username: string, password: string): Promise<void> {
  saveSession(await readJson<TokenResponse>(await postJson('/api/auth/token', { username, password })))
}

// Giriş ekranı ayarları (Google Client ID). Token gerektirmez.
export const getAuthConfig = async (): Promise<AuthConfigResponse> =>
  readJson<AuthConfigResponse>(await apiFetch('/api/auth/config'))

// Kayıt başarılı olursa doğrudan giriş yapılmış olur.
export async function register(userName: string, email: string, password: string, displayName: string): Promise<void> {
  const body = { userName, email, password, displayName: displayName || null }
  saveSession(await readJson<TokenResponse>(await postJson('/api/auth/register', body)))
}

export async function loginWithGoogle(idToken: string): Promise<void> {
  saveSession(await readJson<TokenResponse>(await postJson('/api/auth/google', { idToken })))
}

// Şifresiz misafir girişi: yalnızca bugünün Süper Lig maçları görülür (kısıtlar API'de).
export async function loginAsGuest(): Promise<void> {
  saveSession(await readJson<TokenResponse>(await postJson('/api/auth/guest', {})))
}

// E-posta kayıtlı olmasa da başarılı döner (hangi e-postaların kayıtlı olduğu anlaşılmasın).
export async function forgotPassword(email: string): Promise<void> {
  const res = await postJson('/api/auth/forgot-password', { email })
  if (!res.ok) await readJson(res)
}

export async function resetPassword(token: string, password: string): Promise<void> {
  const res = await postJson('/api/auth/reset-password', { token, password })
  if (!res.ok) await readJson(res)
}

// Sunucudaki refresh token iptal edilir; istek başarısız olsa bile yerel oturum kapanır.
export async function logout(): Promise<void> {
  const refreshToken = readRefreshToken()
  clearSession()
  if (refreshToken) await postJson('/api/auth/revoke', { refreshToken }).catch(() => undefined)
}

function refreshAccessToken(): Promise<string | null> {
  if (!refreshing) {
    refreshing = (async () => {
      const refreshToken = readRefreshToken()
      if (!refreshToken) return null
      const res = await postJson('/api/auth/refresh', { refreshToken }).catch(() => null)
      if (!res?.ok) {
        // 401: refresh token geçersiz/süresi dolmuş -> oturum biter. Ağ hatasında oturum korunur, sonra tekrar denenir.
        if (res?.status === 401) clearSession()
        return null
      }
      const tokens = await res.json() as TokenResponse
      saveSession(tokens)
      return tokens.accessToken
    })().finally(() => { refreshing = null })
  }
  return refreshing
}

function sessionExpired(): never {
  clearSession()
  sessionExpiredHandler?.()
  throw new Error(t('api.sessionExpired'))
}

// body verilirse JSON olarak gönderilir (POST/PUT)
const send = (url: string, token: string, method: string, body?: unknown) =>
  apiFetch(url, {
    method,
    headers: body === undefined
      ? { Authorization: `Bearer ${token}` }
      : { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body)
  })

async function request<T>(url: string, method = 'GET', body?: unknown): Promise<T> {
  let token = accessToken ?? await refreshAccessToken()
  if (!token) sessionExpired()

  let res = await send(url, token, method, body)

  if (res.status === 401) {
    // Access token süresi dolmuş: bir kez yenileyip tekrar dene. Başka bir istek zaten yenilediyse onu kullan.
    if (accessToken === token) accessToken = null
    token = accessToken ?? await refreshAccessToken()
    if (!token) sessionExpired()
    res = await send(url, token, method, body)
    if (res.status === 401) sessionExpired()
  }

  return readJson<T>(res)
}

export const getMatches = (date: string) =>
  request<MatchSummary[]>(`/api/matches?date=${date}`)

export const getForecast = (id: number, refresh = false) =>
  request<ForecastResult>(`/api/matches/${id}/forecast${refresh ? '?refresh=true' : ''}`)

export const getMe = () =>
  request<MeResponse>('/api/me')

export const clearCache = () =>
  request<CacheClearResponse>('/api/cache', 'DELETE')

// Kullanıcının favori ligleri ("Ülke / Lig" anahtarları). Ekleme/silme güncel listeyi döner.
const favoriteLeaguesUrl = (league: string) => `/api/me/favorite-leagues?league=${encodeURIComponent(league)}`

export const getFavoriteLeagues = () =>
  request<string[]>('/api/me/favorite-leagues')

export const addFavoriteLeague = (league: string) =>
  request<string[]>(favoriteLeaguesUrl(league), 'PUT')

export const removeFavoriteLeague = (league: string) =>
  request<string[]>(favoriteLeaguesUrl(league), 'DELETE')

/*
 * Admin paneli. Yetkiler API'de kontrol edilir (users.view / users.manage / roles.manage / groups.manage /
 * permissions.manage); yetkisiz istek 403 ve ProblemDetails mesajıyla hata fırlatır.
 * Atama uçları (…/roles, …/groups, …/users, …/permissions) listeyi verilen id'lerle değiştirir.
 */
export const getUsers = () => request<UserResponse[]>('/api/users')
export const createUser = (body: CreateUserRequest) => request<UserResponse>('/api/users', 'POST', body)
export const updateUser = (id: number, body: UpdateUserRequest) => request<void>(`/api/users/${id}`, 'PUT', body)
// Kullanıcının tüm açık oturumlarını kapatır
export const setUserPassword = (id: number, password: string) => request<void>(`/api/users/${id}/password`, 'PUT', { password })
export const deleteUser = (id: number) => request<void>(`/api/users/${id}`, 'DELETE')
export const setUserRoles = (id: number, roleIds: number[]) => request<void>(`/api/users/${id}/roles`, 'PUT', { roleIds })
export const setUserGroups = (id: number, groupIds: number[]) => request<void>(`/api/users/${id}/groups`, 'PUT', { groupIds })

export const getRoles = () => request<RoleResponse[]>('/api/roles')
export const createRole = (body: RoleRequest) => request<RoleResponse>('/api/roles', 'POST', body)
export const updateRole = (id: number, body: RoleRequest) => request<void>(`/api/roles/${id}`, 'PUT', body)
// force=false iken role atanmış kullanıcı/grup varsa API 409 döner
export const deleteRole = (id: number, force = false) => request<void>(`/api/roles/${id}${force ? '?force=true' : ''}`, 'DELETE')
export const setRolePermissions = (id: number, permissionIds: number[]) => request<void>(`/api/roles/${id}/permissions`, 'PUT', { permissionIds })

export const getGroups = () => request<GroupResponse[]>('/api/groups')
export const createGroup = (body: GroupRequest) => request<GroupResponse>('/api/groups', 'POST', body)
export const updateGroup = (id: number, body: GroupRequest) => request<void>(`/api/groups/${id}`, 'PUT', body)
export const deleteGroup = (id: number) => request<void>(`/api/groups/${id}`, 'DELETE')
export const setGroupRoles = (id: number, roleIds: number[]) => request<void>(`/api/groups/${id}/roles`, 'PUT', { roleIds })
export const setGroupUsers = (id: number, userIds: number[]) => request<void>(`/api/groups/${id}/users`, 'PUT', { userIds })

export const getPermissions = () => request<PermissionResponse[]>('/api/permissions')
export const createPermission = (body: CreatePermissionRequest) => request<PermissionResponse>('/api/permissions', 'POST', body)
export const updatePermission = (id: number, body: UpdatePermissionRequest) => request<void>(`/api/permissions/${id}`, 'PUT', body)
export const deletePermission = (id: number) => request<void>(`/api/permissions/${id}`, 'DELETE')

// Kayıt ekranında, giriş yapılmadan kullanılır; token gerektirmez.
export const generatePassword = async (length = 16): Promise<PasswordResponse> =>
  readJson<PasswordResponse>(await apiFetch(`/api/password?length=${length}`))