import type { ForecastResult, MatchSummary } from './types'

async function get<T>(url: string): Promise<T> {
  const res = await fetch(url)
  if (!res.ok) {
    // API ProblemDetails döner; "detail" alanı kullanıcıya gösterilebilir mesajdır.
    const problem = await res.json().catch(() => null)
    throw new Error(problem?.detail ?? `İstek başarısız oldu (HTTP ${res.status}).`)
  }
  return res.json() as Promise<T>
}

export const getMatches = (date: string) =>
  get<MatchSummary[]>(`/api/matches?date=${date}`)

export const getForecast = (id: number, refresh = false) =>
  get<ForecastResult>(`/api/matches/${id}/forecast${refresh ? '?refresh=true' : ''}`)
