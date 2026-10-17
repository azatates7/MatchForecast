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
  label: string
  market: string
  selection: string
  /** Hesaplanan gerçekleşme olasılığı (0-100) */
  confidence: number
  reasoning: string
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
  label: string
  /** Beklenen değer (ör. 2.6 gol) */
  expected: number
  /** Olası aralık: dağılımın ortadaki %80'i */
  rangeMin: number
  rangeMax: number
  /** Korner için her zaman boş */
  lines: LineProbability[]
  reasoning: string
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
  summary: string
  outcomes: OutcomeProbabilities
  stats: StatProjection[]
  predictions: Prediction[]
  generatedAt: string
}

export interface TokenResponse {
  accessToken: string
  tokenType: string
  expiresAt: string
}

export interface CacheClearResponse {
  deletedKeys: number
}

export interface PasswordResponse {
  password: string
  length: number
}