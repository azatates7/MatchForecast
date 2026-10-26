import { useEffect, useRef, useState } from 'react'

interface Props {
  /** [lig anahtarı, maç sayısı] çiftleri; aramaya uyan maçlardan, alfabetik */
  options: [string, number][]
  /** Seçili lig anahtarları; boş dizi = "Tümü" (filtre yok) */
  selected: string[]
  /** Aramaya uyan toplam maç sayısı ("Tüm ligler (n)") */
  total: number
  /** Seçili liglerdeki maç sayısı ("Seçili Ligler (n)") */
  selectedCount: number
  onSelectAll: () => void
  onToggle: (key: string) => void
}

/*
 * Çoklu lig / ülke filtresi. Native <select multiple> Ctrl+tık istediği için kutucuklu açılır liste kullanılır.
 * - "Tümü" işaretliyken filtre yoktur; bir lig seçilince "Tümü" kendiliğinden kalkar.
 * - "Tümü" seçilince (veya son lig de kaldırılınca) lig filtreleri temizlenir.
 * Dışarı tıklayınca veya Escape ile kapanır; lig seçerken açık kalır ki birden fazla lig art arda seçilebilsin.
 */
export default function LeagueFilter({ options, selected, total, selectedCount, onSelectAll, onToggle }: Props) {
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false)
    }
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  const all = selected.length === 0
  const disabled = options.length === 0

  return (
    <div className="league-multi" ref={rootRef}>
      <button
        type="button"
        className="league-filter league-multi-toggle"
        onClick={() => setOpen(o => !o)}
        aria-haspopup="true"
        aria-expanded={open}
        aria-label="Lig / turnuva filtresi"
        disabled={disabled}
      >
        <span>{all ? `Tüm ligler (${total})` : `Seçili Ligler (${selectedCount})`}</span>
        <svg className="league-multi-chevron" viewBox="0 0 24 24" width="16" height="16" aria-hidden="true">
          <path fill="currentColor" d="M7.4 8.6 12 13.2l4.6-4.6L18 10l-6 6-6-6z" />
        </svg>
      </button>
      {open && !disabled && (
        <div className="league-multi-panel" role="group" aria-label="Ligler">
          <label className="league-multi-option league-multi-all">
            <input type="checkbox" checked={all} onChange={onSelectAll} />
            <span className="league-multi-name">Tümü</span>
            <span className="league-multi-count">{total}</span>
          </label>
          {options.map(([key, count]) => (
            <label key={key} className="league-multi-option">
              <input type="checkbox" checked={selected.includes(key)} onChange={() => onToggle(key)} />
              <span className="league-multi-name">{key}</span>
              <span className="league-multi-count">{count}</span>
            </label>
          ))}
        </div>
      )}
    </div>
  )
}