export interface MatchSummary {
  id: number
  kickoff: string
  league: string
  country: string
  homeTeam: string
  awayTeam: string
  status: string
}

export interface Prediction {
  rank: number
  label: string
  market: string
  selection: string
  odd: number
  impliedProbability: number
  confidence: number
  reasoning: string
}

export interface ForecastResult {
  match: MatchSummary
  bookmaker: string
  summary: string
  predictions: Prediction[]
  generatedAt: string
}
