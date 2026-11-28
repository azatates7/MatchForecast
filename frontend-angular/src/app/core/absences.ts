import type { AbsenceReport, AbsenceSource, LocalizedText, MatchSummary, RefereeInfo, TeamAbsence, TeamForm } from './types'

/*
 * Eksik oyuncuları ev/deplasman takımına göre gruplar ve etiketler. Saf mantık; React ve Angular aynı dosyayı kullanır (frontend/src/Absences.ts = frontend-angular/src/app/core/absences.ts).
 * Liste maç günü resmi açıklamalardan backend'de çıkarılır; durumlar: Cezalı, Kadroda yok, Sakat, Şüpheli.
 */
export type AbsenceKind = 'suspended' | 'not-in-squad' | 'injured' | 'doubtful'

export interface Absence {
  player: string
  kind: AbsenceKind
  reason: LocalizedText
}

export const ABSENCE_LABEL = {
  suspended: 'absences.suspended',
  'not-in-squad': 'absences.notInSquad',
  injured: 'absences.injured',
  doubtful: 'absences.doubtful'
} as const

export function absenceKind(status: TeamAbsence['status']): AbsenceKind {
  switch (status) {
    case 'Suspended': return 'suspended'
    case 'NotInSquad': return 'not-in-squad'
    case 'Injured': return 'injured'
    default: return 'doubtful'
  }
}

export function groupAbsences(match: MatchSummary, players: TeamAbsence[]): { team: string; absences: Absence[] }[] {
  return [match.homeTeam, match.awayTeam].map(team => ({
    team,
    absences: players
      .filter(p => p.team === team)
      .map(p => ({ player: p.player, kind: absenceKind(p.status), reason: p.reason }))
  }))
}

// Eksik listesi gösterilebilir mi: maç günü taraması veya maç öncesi ön bilgi.
export function hasAbsenceList(report: AbsenceReport | null | undefined): boolean {
  return report?.state === 'Ready' || report?.state === 'Preliminary'
}

// Dipnot: tahminin hangi eksik oyuncu (ve hakem) bilgisiyle üretildiğini söyler.
export function absenceFootnote(report: AbsenceReport | null | undefined) {
  if (report?.state === 'Preliminary') return 'forecast.footnotePreliminary' as const
  if (report?.state === 'Ready' && refereeUsedInForecast(report.referee)) return 'forecast.footnoteReferee' as const
  if (report?.state === 'Ready') return 'forecast.footnote' as const
  if (report?.state === 'Pending') return 'forecast.footnotePending' as const
  return 'forecast.footnoteNoAbsences' as const
}

// ---- Hakem ----

export const STRICTNESS_LABEL = {
  Strict: 'referee.strict',
  Balanced: 'referee.balanced',
  Lenient: 'referee.lenient'
} as const

export const STRICTNESS_CLASS = {
  Strict: 'referee-strict',
  Balanced: 'referee-balanced',
  Lenient: 'referee-lenient'
} as const

const STAT_LABEL = {
  yellowPerMatch: 'referee.yellow',
  redPerMatch: 'referee.red',
  penaltiesPerMatch: 'referee.penalties',
  foulsPerMatch: 'referee.fouls'
} as const

// Bulunan maç başı ortalamalar, gösterim sırasıyla (sarı, kırmızı, penaltı, faul); bulunmayanlar atlanır.
export function refereeStats(r: RefereeInfo): { label: (typeof STAT_LABEL)[keyof typeof STAT_LABEL]; value: number }[] {
  return (Object.keys(STAT_LABEL) as (keyof typeof STAT_LABEL)[])
    .filter(k => r[k] !== null && r[k] !== undefined)
    .map(k => ({ label: STAT_LABEL[k], value: r[k] as number }))
}

// Backend yalnızca istatistik veya tarz bulunduysa hakemi prompt'a koyar; dipnot da buna göre değişir.
export function refereeUsedInForecast(r: RefereeInfo | null | undefined): boolean {
  return !!r && (refereeStats(r).length > 0 || !!r.style?.tr)
}

// Kaynak adı: başlık (genelde alan adı) yoksa URL'nin host'u.
export function sourceLabel(s: AbsenceSource): string {
  if (s.title) return s.title
  try { return new URL(s.url).hostname } catch { return s.url }
}

// ---- Maç bağlamı kartı (form ve dinlenme) ----

export type FormResult = 'W' | 'D' | 'L'

export const FORM_LABEL = { W: 'context.result.W', D: 'context.result.D', L: 'context.result.L' } as const
export const FORM_NAME = { W: 'context.resultName.W', D: 'context.resultName.D', L: 'context.resultName.L' } as const

// Bu kadar gün veya daha az dinlenen takım "yoğun fikstür" etiketi alır (backend prompt kuralıyla aynı eşik).
export const CONGESTED_DAYS = 3

export interface TeamContext {
  team: string
  home: boolean
  form: FormResult[]
  position: number | null
  venueMatches: number | null
  goalsFor: number | null
  goalsAgainst: number | null
  xgFor: number | null
  xgAgainst: number | null
  restDays: number | null
  previousCompetition: string | null
  congested: boolean
}

// Maçın İstanbul takvim günü (YYYY-MM-DD); dinlenme süresi takvim günü farkıdır (backend ile aynı).
const istanbulDay = (iso: string) => {
  const parts = new Intl.DateTimeFormat('en-US', { timeZone: 'Europe/Istanbul', year: 'numeric', month: '2-digit', day: '2-digit' })
    .formatToParts(new Date(iso))
  const get = (type: string) => parts.find(p => p.type === type)?.value ?? ''
  return `${get('year')}-${get('month')}-${get('day')}`
}

const dayDiff = (from: string, to: string) =>
  Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000)

/*
 * Kartın satırları: ev sahibi önce. Hiç bilgisi olmayan takım listeye girmez; liste boşsa kart gösterilmez.
 * Dinlenme süresi maç günü ile son maç günü arasındaki takvim günü farkıdır (geçersiz/gelecek tarih yok sayılır).
 */
export function teamContexts(report: AbsenceReport | null | undefined, match: MatchSummary): TeamContext[] {
  const matchDay = istanbulDay(match.kickoff)
  return (report?.form ?? [])
    .map((f: TeamForm): TeamContext => {
      const rest = f.lastMatchDate ? dayDiff(f.lastMatchDate, matchDay) : null
      const restDays = rest !== null && rest > 0 ? rest : null
      return {
        team: f.team,
        home: f.team === match.homeTeam,
        form: (f.last5 ?? '').split('').filter((c): c is FormResult => c === 'W' || c === 'D' || c === 'L'),
        position: f.leaguePosition,
        venueMatches: f.venueMatches,
        goalsFor: f.venueGoalsFor,
        goalsAgainst: f.venueGoalsAgainst,
        xgFor: f.xgFor,
        xgAgainst: f.xgAgainst,
        restDays,
        previousCompetition: restDays !== null ? f.lastMatchCompetition ?? null : null,
        congested: restDays !== null && restDays <= CONGESTED_DAYS
      }
    })
    .filter(c => c.form.length > 0 || c.position !== null || c.goalsFor !== null || c.goalsAgainst !== null
      || c.xgFor !== null || c.xgAgainst !== null || c.restDays !== null)
    .sort((a, b) => Number(b.home) - Number(a.home))
}

// Formu olan takımlar (form satırı) ve dinlenme bilgisi olan takımlar (dinlenme satırı) ayrı gösterilir.
export const hasFormLine = (c: TeamContext) =>
  c.form.length > 0 || c.position !== null || c.goalsFor !== null || c.goalsAgainst !== null || c.xgFor !== null || c.xgAgainst !== null