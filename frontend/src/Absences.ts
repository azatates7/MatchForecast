import type { AbsenceReport, AbsenceSource, LocalizedText, MatchSummary, TeamAbsence } from './types'

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

// Dipnot: tahminin hangi eksik oyuncu bilgisiyle üretildiğini söyler.
export function absenceFootnote(report: AbsenceReport | null | undefined) {
  if (report?.state === 'Ready') return 'forecast.footnote' as const
  if (report?.state === 'Pending') return 'forecast.footnotePending' as const
  return 'forecast.footnoteNoAbsences' as const
}

// Kaynak adı: başlık (genelde alan adı) yoksa URL'nin host'u.
export function sourceLabel(s: AbsenceSource): string {
  if (s.title) return s.title
  try { return new URL(s.url).hostname } catch { return s.url }
}