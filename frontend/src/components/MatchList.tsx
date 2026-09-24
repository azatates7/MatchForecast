import type { MatchSummary } from '../types'

interface Props {
  matches: MatchSummary[]
  loading: boolean
  error?: string
  selectedId: number | null
  onSelect: (id: number) => void
}

const time = (iso: string) =>
  new Date(iso).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' })

export default function MatchList({ matches, loading, error, selectedId, onSelect }: Props) {
  if (loading) return <p className="note">Maçlar yükleniyor…</p>
  if (error) return <p className="note note-error">{error}</p>
  if (matches.length === 0) return <p className="note">Bu tarihte maç yok. Başka bir gün seçin.</p>

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
            {items.map(m => (
              <li key={m.id}>
                <button
                  type="button"
                  className={`match${m.id === selectedId ? ' is-selected' : ''}`}
                  onClick={() => onSelect(m.id)}
                  aria-pressed={m.id === selectedId}
                >
                  <span className="match-time">{time(m.kickoff)}</span>
                  <span className="match-teams">
                    <span>{m.homeTeam}</span>
                    <span>{m.awayTeam}</span>
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </div>
      ))}
    </div>
  )
}
