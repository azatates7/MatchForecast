import type { MatchSummary, MissingPlayer } from './types'

/*
 * Eksik oyuncuları ev/deplasman takımına göre gruplar ve etiketler. Saf mantık; React ve Angular aynı dosyayı kullanır
 * (frontend/src/Absences.ts = frontend-angular/src/app/core/absences.ts).
 * Etiket, sağlayıcının İngilizce "reason" metninden çıkarılır: ceza (suspended, red/yellow card) → Cezalı,
 * sakatlık/hastalık → Sakat, "Questionable" → Şüpheli, diğer nedenler (ör. milli takım) → Oynamayacak.
 */
export type AbsenceKind = 'suspended' | 'injured' | 'doubtful' | 'out'

export interface Absence {
  player: string
  kind: AbsenceKind
  /** Gösterilecek neden (İngilizce, sağlayıcı metni); etiketle aynı bilgiyi veriyorsa boş */
  reason: string
}

export const ABSENCE_LABEL = {
  suspended: 'absences.suspended',
  injured: 'absences.injured',
  doubtful: 'absences.doubtful',
  out: 'absences.out'
} as const

const SUSPENSION = /suspen|red card|yellow card/i
const INJURY = /injur|knock|ill|surgery|fracture|strain|sprain|muscle|knee|ankle|hamstring|groin|calf|thigh|back|shoulder|foot|hip/i

export function absenceKind(p: MissingPlayer): AbsenceKind {
  if (p.type === 'Questionable') return 'doubtful'
  if (SUSPENSION.test(p.reason)) return 'suspended'
  if (INJURY.test(p.reason)) return 'injured'
  return 'out'
}

export function groupAbsences(match: MatchSummary, players: MissingPlayer[]): { team: string; absences: Absence[] }[] {
  return [match.homeTeam, match.awayTeam].map(team => ({
    team,
    absences: players
      .filter(p => p.team.toLowerCase() === team.toLowerCase())
      .map(p => ({ player: p.player, kind: absenceKind(p), reason: /^suspended$/i.test(p.reason.trim()) ? '' : p.reason }))
  }))
}