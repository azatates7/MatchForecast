import type { AbsenceReport, AbsenceSource, LocalizedText, MatchSummary, RefereeInfo, TeamAbsence } from './types'

/*
 * Eksik oyuncuları ev/deplasman takımına göre gruplar ve etiketler. Saf mantık; React ve Angular aynı dosyayı kullanır
 * (frontend/src/Absences.ts = frontend-angular/src/app/core/absences.ts).
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