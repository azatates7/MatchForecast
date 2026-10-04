import type { ForecastResult, MatchSummary, TokenResponse } from './types'

// Token yalnızca bellekte tutulur: sayfa yenilenince yeniden alınır (ucuz), localStorage'a yazılmadığı için XSS ile okunamaz.
// Promise saklanır ki aynı anda başlayan istekler tek bir /api/auth/token çağrısını paylaşsın.
let tokenRequest: Promise<string> | null = null

async function readJson<T>(res: Response): Promise<T> {
  if (!res.ok) {
    // API ProblemDetails döner; "detail" alanı kullanıcıya gösterilebilir mesajdır.
    const problem = await res.json().catch(() => null)
    throw new Error(problem?.detail ?? `İstek başarısız oldu (HTTP ${res.status}).`)
  }
  return res.json() as Promise<T>
}

function currentToken(): Promise<string> {
  if (!tokenRequest) {
    const request = fetch('/api/auth/token')
      .then(res => readJson<TokenResponse>(res))
      .then(t => t.accessToken)
    // Token alınamazsa hatalı promise önbellekte kalmasın; sonraki istek yeniden denesin.
    request.catch(() => { if (tokenRequest === request) tokenRequest = null })
    tokenRequest = request
  }
  return tokenRequest
}

const send = (url: string, token: string) =>
  fetch(url, { headers: { Authorization: `Bearer ${token}` } })

async function get<T>(url: string): Promise<T> {
  const token = currentToken()
  let res = await send(url, await token)

  if (res.status === 401) {
    // Token süresi dolmuş veya API yeniden başlayıp SecretKey değişmiş olabilir: bir kez yenileyip tekrar dene.
    // Sadece hâlâ aynı token kullanılıyorsa sıfırla; paralel istekler token'ı birden fazla kez yenilemesin.
    if (tokenRequest === token) tokenRequest = null
    res = await send(url, await currentToken())
  }

  return readJson<T>(res)
}

export const getMatches = (date: string) =>
  get<MatchSummary[]>(`/api/matches?date=${date}`)

export const getForecast = (id: number, refresh = false) =>
  get<ForecastResult>(`/api/matches/${id}/forecast${refresh ? '?refresh=true' : ''}`)