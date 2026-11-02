import { LANGS } from '../i18n/i18n'
import { useI18n } from '../i18n/useI18n'

/*
 * TR | EN dil seçici (üst bar ve giriş ekranı). Seçili dil vurgulanır; seçim localStorage'da saklanır.
 * Site adı da dile göre değişir: TR -> "Maç Analiz", EN -> "Match Forecast".
 */
export default function LanguageToggle() {
  const { lang, setLang, t } = useI18n()

  return (
    <div className="lang-toggle" role="group" aria-label={t('lang.label')}>
      {LANGS.map(l => (
        <button
          key={l}
          type="button"
          className={`lang-btn${l === lang ? ' is-active' : ''}`}
          aria-pressed={l === lang}
          title={t(`lang.${l}`)}
          onClick={() => setLang(l)}
        >
          {l.toUpperCase()}
        </button>
      ))}
    </div>
  )
}
