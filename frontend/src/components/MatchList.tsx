import type { MatchSummary } from '../types'
import { isLive, minuteLabel } from './MatchStatus'
import { useI18n } from '../i18n/useI18n'

interface Props {
  matches: MatchSummary[]
  loading: boolean
  error?: string
  selectedId: number | null
  onSelect: (id: number) => void
  /** Listenin API'den çekildiği an (ms); canlı dakikayı ilerletmek için */
  fetchedAt: number
  /** Ekrandaki saat (ms); App tarafından periyodik güncellenir */
  now: number
  /** Liste boşken gösterilecek metin (misafir gün/lig seçemediği için ona özel metin verilir) */
  emptyText?: string
}

const time = (iso: string, locale: string) =>
  new Date(iso).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' })

export default function MatchList({ matches, loading, error, selectedId, onSelect, fetchedAt, now, emptyText }: Props) {
  const { t, locale } = useI18n()
  if (loading) return <p className="note">{t('matches.loading')}</p>
  if (error) return <p className="note note-error">{error}</p>
  if (matches.length === 0) return <p className="note">{emptyText ?? t('matches.empty')}</p>

  // Lig bazında grupla, sıralama API'den gelen başlama saatine göre korunur
  const groups = new Map<string, MatchSummary[]>()
  for (const m of matches) {
    const key = `${m.country} / ${m.league}`
    groups.set(key, [...(groups.get(key) ?? []), m])
  }

  return (
    <div className="match-groups">
      {[...groups].map(([league, items]) => (
        <div key={league} className="league">
          <h2 className="league-name">{league}</h2>
          <ul>
            {items.map(m => {
              const live = isLive(m)
              return (
                <li key={m.id}>
                  <button
                    type="button"
                    className={`match${m.id === selectedId ? ' is-selected' : ''}`}
                    onClick={() => onSelect(m.id)}
                    aria-pressed={m.id === selectedId}
                  >
                    {live ? (
                      <span className="match-time match-live" aria-label={t('matches.live', { minute: minuteLabel(m, fetchedAt, now) })}>
                        {minuteLabel(m, fetchedAt, now)}
                      </span>
                    ) : (
                      <span className="match-time">{time(m.kickoff, locale)}</span>
                    )}
                    <span className="match-teams">
                      <span>{m.homeTeam}</span>
                      <span>{m.awayTeam}</span>
                    </span>
                  </button>
                </li>
              )
            })}
          </ul>
        </div>
      ))}
    </div>
  )
}