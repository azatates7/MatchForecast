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
  odd: number | null
  impliedProbability: number | null
  confidence: number
  reasoning: string
}

export interface ForecastResult {
  match: MatchSummary
  bookmaker: string | null
  summary: string
  predictions: Prediction[]
  generatedAt: string
}