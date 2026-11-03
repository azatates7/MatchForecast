import { useEffect, useRef, useState } from 'react'
import { useI18n } from '../i18n/useI18n'

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
  /** Kullanıcının favori lig anahtarları */
  favorites: string[]
  /** "Favori ligler" seçilince: listede olan favori liglerin anahtarları */
  onSelectFavorites: (keys: string[]) => void
  onToggleFavorite: (key: string) => void
}

const STAR = 'M12 17.27 18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z'
const STAR_OUTLINE = 'M22 9.24l-7.19-.62L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21 12 17.27 18.18 21l-1.63-7.03L22 9.24zM12 15.4l-3.76 2.27 1-4.28-3.32-2.88 4.38-.38L12 6.1l1.71 4.04 4.38.38-3.32 2.88 1 4.28L12 15.4z'

const StarIcon = ({ filled, className }: { filled: boolean; className?: string }) => (
  <svg className={className} viewBox="0 0 24 24" width="16" height="16" aria-hidden="true">
    <path fill="currentColor" d={filled ? STAR : STAR_OUTLINE} />
  </svg>
)

/*
 * Çoklu lig / ülke filtresi. Native <select multiple> Ctrl+tık istediği için kutucuklu açılır liste kullanılır.
 * - "Tümü" işaretliyken filtre yoktur; bir lig seçilince "Tümü" kendiliğinden kalkar.
 * - "Tümü" seçilince (veya son lig de kaldırılınca) lig filtreleri temizlenir.
 * - "Favori ligler": listede olan favori ligleri seçer; tam olarak onlar seçiliyken işaretli görünür, tekrar tıklanınca "Tümü"ye döner.
 * - Her ligin yanındaki yıldız ligi favorilere ekler/çıkarır (seçimi değiştirmez).
 * Dışarı tıklayınca veya Escape ile kapanır; lig seçerken açık kalır ki birden fazla lig art arda seçilebilsin.
 */
export default function LeagueFilter({ options, selected, total, selectedCount, onSelectAll, onToggle, favorites, onSelectFavorites, onToggleFavorite }: Props) {
  const { t } = useI18n()
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

  const favoriteOptions = options.filter(([key]) => favorites.includes(key))
  const favoriteKeys = favoriteOptions.map(([key]) => key)
  const favoriteCount = favoriteOptions.reduce((sum, [, count]) => sum + count, 0)
  const favoritesActive = favoriteKeys.length > 0 && selected.length === favoriteKeys.length && favoriteKeys.every(k => selected.includes(k))

  const label = all
    ? t('league.allLeagues', { n: total })
    : favoritesActive
      ? t('league.favoriteLeagues', { n: selectedCount })
      : t('league.selectedLeagues', { n: selectedCount })

  return (
    <div className="league-multi" ref={rootRef}>
      <button
        type="button"
        className="league-filter league-multi-toggle"
        onClick={() => setOpen(o => !o)}
        aria-haspopup="true"
        aria-expanded={open}
        aria-label={t('league.aria')}
        disabled={disabled}
      >
        <span>{label}</span>
        <svg className="league-multi-chevron" viewBox="0 0 24 24" width="16" height="16" aria-hidden="true">
          <path fill="currentColor" d="M7.4 8.6 12 13.2l4.6-4.6L18 10l-6 6-6-6z" />
        </svg>
      </button>
      {open && !disabled && (
        <div className="league-multi-panel" role="group" aria-label={t('league.group')}>
          <label className="league-multi-option league-multi-all">
            <input type="checkbox" checked={all} onChange={onSelectAll} />
            <span className="league-multi-name">{t('league.all')}</span>
            <span className="league-multi-count">{total}</span>
          </label>
          {favoriteKeys.length > 0 && (
            <label className="league-multi-option league-multi-all">
              <input
                type="checkbox"
                checked={favoritesActive}
                onChange={() => (favoritesActive ? onSelectAll() : onSelectFavorites(favoriteKeys))}
              />
              <StarIcon filled className="league-multi-fav-icon" />
              <span className="league-multi-name">{t('league.favorites')}</span>
              <span className="league-multi-count">{favoriteCount}</span>
            </label>
          )}
          {options.map(([key, count]) => {
            const isFavorite = favorites.includes(key)
            return (
              <label key={key} className="league-multi-option">
                <input type="checkbox" checked={selected.includes(key)} onChange={() => onToggle(key)} />
                <span className="league-multi-name">{key}</span>
                <span className="league-multi-count">{count}</span>
                {/* Label içindeki buton: tıklama kutucuğu değiştirmesin diye varsayılan davranış engellenir. */}
                <button
                  type="button"
                  className="league-multi-star"
                  aria-pressed={isFavorite}
                  aria-label={t(isFavorite ? 'league.removeFavorite' : 'league.addFavorite')}
                  title={t(isFavorite ? 'league.removeFavorite' : 'league.addFavorite')}
                  onClick={e => { e.preventDefault(); onToggleFavorite(key) }}
                >
                  <StarIcon filled={isFavorite} />
                </button>
              </label>
            )
          })}
        </div>
      )}
    </div>
  )
}