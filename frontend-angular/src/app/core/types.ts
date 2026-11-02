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
}

export interface CacheClearResponse {
  deletedKeys: number
}

export interface PasswordResponse {
  password: string
  length: number
}