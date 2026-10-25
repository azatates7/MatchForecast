import type { AuthConfigResponse, CacheClearResponse, ForecastResult, MatchSummary, MeResponse, PasswordResponse, TokenResponse } from './types'

/*
 * Oturum:
 * - Access token yalnızca bellekte tutulur (kısa ömürlü; XSS ile kalıcı olarak ele geçirilemez).
 * - Refresh token localStorage'da tutulur ki sayfa yenilenince tekrar giriş gerekmesin. Bedeli: XSS olursa okunabilir.
 *   Daha güvenli seçenek httpOnly cookie'dir (backend değişikliği gerekir).
 * - Access token yoksa veya 401 alınırsa refresh token ile yenisi alınır; o da başarısız olursa oturum kapanır
 *   ve onSessionExpired ile uygulamaya haber verilir (login ekranı gösterilir).
 */
const REFRESH_KEY = 'matchforecast.refreshToken'

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
    throw new Error(problem?.detail ?? validation ?? `İstek başarısız oldu (HTTP ${res.status}).`)
  }
  return res.json() as Promise<T>
}

const postJson = (url: string, body: unknown) =>
  fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })

export async function login(username: string, password: string): Promise<void> {
  saveSession(await readJson<TokenResponse>(await postJson('/api/auth/token', { username, password })))
}

// Giriş ekranı ayarları (Google Client ID). Token gerektirmez.
export const getAuthConfig = async (): Promise<AuthConfigResponse> =>
  readJson<AuthConfigResponse>(await fetch('/api/auth/config'))

// Kayıt başarılı olursa doğrudan giriş yapılmış olur.
export async function register(userName: string, email: string, password: string, displayName: string): Promise<void> {
  const body = { userName, email, password, displayName: displayName || null }
  saveSession(await readJson<TokenResponse>(await postJson('/api/auth/register', body)))
}

export async function loginWithGoogle(idToken: string): Promise<void> {
  saveSession(await readJson<TokenResponse>(await postJson('/api/auth/google', { idToken })))
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
  throw new Error('Oturumun süresi doldu, lütfen tekrar giriş yapın.')
}

const send = (url: string, token: string, method: string) =>
  fetch(url, { method, headers: { Authorization: `Bearer ${token}` } })

async function request<T>(url: string, method = 'GET'): Promise<T> {
  let token = accessToken ?? await refreshAccessToken()
  if (!token) sessionExpired()

  let res = await send(url, token, method)

  if (res.status === 401) {
    // Access token süresi dolmuş: bir kez yenileyip tekrar dene. Başka bir istek zaten yenilediyse onu kullan.
    if (accessToken === token) accessToken = null
    token = accessToken ?? await refreshAccessToken()
    if (!token) sessionExpired()
    res = await send(url, token, method)
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

// Kayıt ekranında, giriş yapılmadan kullanılır; token gerektirmez.
export const generatePassword = async (length = 16): Promise<PasswordResponse> =>
  readJson<PasswordResponse>(await fetch(`/api/password?length=${length}`))