import { useState } from 'react'

/*
 * Aydınlık / karanlık tema düğmesi. Düğme o an AÇIK olan temayı gösterir:
 * aydınlıkta güneş, karanlıkta ay simgesi. Tıklayınca diğer temaya geçilir.
 * Tema <html data-theme="..."> ile uygulanır; ilk değer index.html'deki betikte (kayıtlı seçim, yoksa sistem tercihi)
 * sayfa boyanmadan önce atanır, böylece açılışta yanlış tema bir an bile görünmez.
 */
export const THEME_KEY = 'matchforecast.theme'

type Theme = 'light' | 'dark'

const currentTheme = (): Theme => (document.documentElement.dataset.theme === 'dark' ? 'dark' : 'light')

export default function ThemeToggle() {
  const [theme, setTheme] = useState<Theme>(currentTheme)
  const next: Theme = theme === 'dark' ? 'light' : 'dark'
  const label = theme === 'dark'
    ? 'Karanlık tema açık (aydınlığa geçmek için tıklayın)'
    : 'Aydınlık tema açık (karanlığa geçmek için tıklayın)'

  const toggle = () => {
    document.documentElement.dataset.theme = next
    // Gizli pencerede/engellendiğinde hata fırlatabilir; o durumda seçim yalnızca bu sayfa açıkken geçerli olur.
    try { localStorage.setItem(THEME_KEY, next) } catch { /* yok say */ }
    setTheme(next)
  }

  return (
    <button type="button" className="cache-btn theme-btn" onClick={toggle} aria-label={label} title={label}>
      {theme === 'dark' ? (
        <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
          <path fill="currentColor" d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8Z" />
        </svg>
      ) : (
        <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
          <circle cx="12" cy="12" r="4.5" fill="currentColor" />
          <path stroke="currentColor" strokeWidth="2" strokeLinecap="round"
            d="M12 2v2M12 20v2M2 12h2M20 12h2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
        </svg>
      )}
    </button>
  )
}