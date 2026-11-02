import { messages, type MessageKey } from './messages'

/*
 * Çerçeveden bağımsız dil durumu. Bu dosya frontend-angular/src/app/core/i18n/i18n.ts ile AYNIDIR.
 * - İlk dil index.html'deki betikte, sayfa boyanmadan önce <html lang>'a yazılır (?lang= > kayıtlı seçim > tarayıcı dili).
 *   Burada oradan okunur; tek kaynak <html lang>'dır.
 * - setLang: <html lang>, sayfa başlığı ve localStorage güncellenir, abonelere haber verilir (React/Angular yeniden çizer).
 * - API isteklerinde Accept-Language olarak gönderilir; backend hata mesajlarını bu dilde döner.
 * - Tahmin metinleri API'den { tr, en } olarak gelir (loc ile seçilir); dil değişince yeniden istek gerekmez.
 */
export type Lang = 'tr' | 'en'
export const LANGS: readonly Lang[] = ['tr', 'en']
export const LANG_KEY = 'matchforecast.lang'

export interface LocalizedText {
  tr: string
  en: string
}

export type Params = Record<string, string | number>

let current: Lang = document.documentElement.lang === 'en' ? 'en' : 'tr'
const listeners = new Set<() => void>()

export const getLang = (): Lang => current

export const subscribeLang = (listener: () => void): (() => void) => {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}

export function setLang(lang: Lang): void {
  if (lang === current) return
  current = lang
  document.documentElement.lang = lang
  document.title = messages[lang].brand
  // Gizli pencerede/engellendiğinde hata fırlatabilir; o durumda seçim yalnızca bu sayfa açıkken geçerli olur.
  try { localStorage.setItem(LANG_KEY, lang) } catch { /* yok say */ }
  listeners.forEach(l => l())
}

export function t(key: MessageKey, params?: Params, lang: Lang = current): string {
  let text = messages[lang][key]
  if (params) for (const [name, value] of Object.entries(params)) text = text.split(`{${name}}`).join(String(value))
  return text
}

// Yüzde yazımı dile göre: Türkçede "%55", İngilizcede "55%".
export const pct = (value: number, lang: Lang = current): string => (lang === 'en' ? `${value}%` : `%${value}`)

// API'den gelen iki dilli metin; seçili dil boşsa Türkçesi gösterilir.
export const loc = (text: LocalizedText | null | undefined, lang: Lang = current): string =>
  text ? (text[lang] || text.tr) : ''

// Intl / toLocale* çağrıları için.
export const locale = (lang: Lang = current): string => (lang === 'en' ? 'en-GB' : 'tr-TR')
