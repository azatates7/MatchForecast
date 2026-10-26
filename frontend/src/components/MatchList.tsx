import type { MatchSummary } from './Types'
import { isLive, minuteLabel } from './MatchStatus'

interface Props {
  matches: MatchSummary[]
  loading: boolean
  error?: string
  selectedId: number | null
  onSelect: (id: number) => void
  /** false ise (misafir) kutucuklar hiç çizilmez, liste eski haliyle görünür */
  selectable?: boolean
  /** İşareti kaldırılmış maç id'leri */
  excluded?: ReadonlySet<number>
  onToggle?: (ids: number[], checked: boolean) => void
  /** Listenin API'den çekildiği an (ms); canlı dakikayı ilerletmek için */
  fetchedAt: number
  /** Ekrandaki saat (ms); App tarafından periyodik güncellenir */
  now: number
}

const time = (iso: string) =>
  new Date(iso).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })

const NONE: ReadonlySet<number> = new Set()

export default function MatchList({ matches, loading, error, selectedId, onSelect, fetchedAt, now, selectable = false, excluded = NONE, onToggle }: Props) {
  if (loading) return <p className="note">Maçlar yükleniyor…</p>
  if (error) return <p className="note note-error">{error}</p>
  if (matches.length === 0) return <p className="note">Gösterilecek maç yok. Başka bir gün veya lig seçin.</p>

  // Lig bazında grupla, sıralama API'den gelen başlama saatine göre korunur
  const groups = new Map<string, MatchSummary[]>()
  for (const m of matches) {
    const key = `${m.country} / ${m.league}`
    groups.set(key, [...(groups.get(key) ?? []), m])
  }

  return (
    <div className="match-groups">
      {[...groups].map(([league, items]) => {
        const checkedInLeague = items.filter(m => !excluded.has(m.id)).length
        return (
        <div key={league} className="league">
          {selectable ? (
            <h2 className="league-name league-name-check">
              <label>
                <input
                  type="checkbox"
                  className="match-check"
                  checked={checkedInLeague === items.length}
                  ref={el => { if (el) el.indeterminate = checkedInLeague > 0 && checkedInLeague < items.length }}
                  onChange={e => onToggle?.(items.map(m => m.id), e.target.checked)}
                />
                {league}
              </label>
            </h2>
          ) : (
            <h2 className="league-name">{league}</h2>
          )}
          <ul>
            {items.map(m => {
              const live = isLive(m)
              const checked = !excluded.has(m.id)
              return (
                <li key={m.id} className={selectable ? `match-row${checked ? '' : ' is-unchecked'}` : undefined}>
                  {selectable && (
                    <input
                      type="checkbox"
                      className="match-check"
                      checked={checked}
                      onChange={e => onToggle?.([m.id], e.target.checked)}
                      aria-label={`${m.homeTeam} - ${m.awayTeam} seç`}
                    />
                  )}
                  <button
                    type="button"
                    className={`match${m.id === selectedId ? ' is-selected' : ''}`}
                    onClick={() => onSelect(m.id)}
                    aria-pressed={m.id === selectedId}
                  >
                    {live ? (
                      <span className="match-time match-live" aria-label={`Canlı, ${minuteLabel(m, fetchedAt, now)}`}>
                        {minuteLabel(m, fetchedAt, now)}
                      </span>
                    ) : (
                      <span className="match-time">{time(m.kickoff)}</span>
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
        )
      })}
    </div>
  )
}