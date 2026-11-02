import { useMemo, useSyncExternalStore } from 'react'
import { getLang, loc, locale, pct, setLang, subscribeLang, t, type LocalizedText, type Params } from './i18n'
import type { MessageKey } from './messages'

/*
 * Bileşenlerde: const { t, lang } = useI18n(). Dil değişince hook'u kullanan her bileşen yeniden çizilir.
 * Dönen fonksiyonlar o anki dile bağlıdır; bileşen dışındaki modüller doğrudan i18n.ts'i kullanır.
 */
export function useI18n() {
  const lang = useSyncExternalStore(subscribeLang, getLang)
  return useMemo(() => ({
    lang,
    setLang,
    t: (key: MessageKey, params?: Params) => t(key, params, lang),
    pct: (value: number) => pct(value, lang),
    loc: (text: LocalizedText | null | undefined) => loc(text, lang),
    locale: locale(lang),
  }), [lang])
}
