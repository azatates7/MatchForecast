import type { MatchSummary } from '../types'
import { t } from '../i18n/i18n'
import type { MessageKey } from '../i18n/messages'

// API-Football "fixture.status.short" kodları. Backend'deki MatchStatus sınıfıyla aynı gruplar.
const LIVE = new Set(['1H', 'HT', '2H', 'ET', 'BT', 'P', 'SUSP', 'INT', 'LIVE'])

// Saatin işlediği durumlar ve o bölümün normal süre sınırı (sonrası "+" ile gösterilir)
const RUNNING: Record<string, number> = { '1H': 45, '2H': 90, 'ET': 120, 'LIVE': 90 }

// Saatin durduğu durumlar için sabit etiketler (sözlük anahtarı; metin seçili dilde)
const PAUSED: Record<string, MessageKey> = {
  HT: 'status.HT',
  BT: 'status.BT',
  P: 'status.P',
  SUSP: 'status.SUSP',
  INT: 'status.INT',
}

export const isLive = (m: MatchSummary) => LIVE.has(m.status)

/**
 * Canlı maçın dakika etiketi, ör. "37'", "45+2'", "İY" / "HT".
 * API verisi en fazla birkaç dakikada bir tazelendiği için, son çekimden bu yana geçen süre
 * dakikaya eklenir; böylece yenilemeler arasında da dakika ilerler.
 */
export function minuteLabel(m: MatchSummary, fetchedAt: number, now: number): string {
  if (PAUSED[m.status]) return t(PAUSED[m.status])

  const limit = RUNNING[m.status]
  if (limit === undefined || m.elapsed == null) return t('status.live')

  const sinceFetch = Math.max(0, Math.floor((now - fetchedAt) / 60000))
  const total = m.elapsed + (m.elapsedExtra ?? 0) + sinceFetch

  return total > limit ? `${limit}+${total - limit}'` : `${total}'`
}