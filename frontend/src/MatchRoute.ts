/*
 * Maç adresi: #match/<fikstürId>. Bağlantı paylaşılabilir; tarayıcının geri/ileri tuşu seçilen maçlar arasında gezer.
 * Router kullanılmaz; App hashchange'i dinler (#admin ile aynı yapı). Maçı görme yetkisini API denetler (ör. misafir yalnızca bugünün Süper Lig maçlarını açabilir), burası yalnızca adresi okur/yazar.
 * Not: frontend-angular/src/app/core/match-route.ts ile bu dosya birbirinin kopyasıdır, birlikte güncelleyin.
 */
export const matchHash = (id: number) => `#match/${id}`

// "#match/1557485" → 1557485; başka her şey (boş, #admin, bozuk sayı) → null
export const parseMatchHash = (hash: string): number | null => {
  const m = /^#match\/(\d{1,10})$/.exec(hash)
  if (!m) return null
  const id = Number(m[1])
  return id > 0 && id <= 2_147_483_647 ? id : null
}

// Paylaşılacak tam adres (ör. https://matchforecast.online/#match/1557485)
export const matchUrl = (id: number) => `${location.origin}${location.pathname}${matchHash(id)}`

// Adresi geçmişe yeni kayıt eklemeden değiştirir (otomatik seçimde geri tuşu boş ekrana dönmesin). hashchange tetiklenmez.
export const replaceHash = (hash: string) => history.replaceState(null, '', hash)

// Bir anın İstanbul takvimindeki günü (YYYY-MM-DD); API ve fikstür tarihleri bu saat dilimindedir.
export const istanbulDate = (d: Date) => {
  const parts = new Intl.DateTimeFormat('en-US', { timeZone: 'Europe/Istanbul', year: 'numeric', month: '2-digit', day: '2-digit' })
    .formatToParts(d)
  const get = (type: string) => parts.find(p => p.type === type)?.value ?? ''
  return `${get('year')}-${get('month')}-${get('day')}`
}

/*
 * Seçilebilir tarihler: matches.any-date yetkisi olmayanlar (Admin dışındakiler) yalnızca bugünü ve yarını (İstanbul)
 * seçebilir; yetkisi olanlar için sınır yok (geçmiş dahil). API de aynı kuralı uygular. ISO tarihler (YYYY-MM-DD)
 * metin olarak karşılaştırılabilir.
 */
export const MATCH_DAYS_AHEAD = 1

export const selectableRange = () => {
  const min = istanbulDate(new Date())
  const d = new Date(`${min}T12:00:00Z`)
  d.setUTCDate(d.getUTCDate() + MATCH_DAYS_AHEAD)
  return { min, max: d.toISOString().slice(0, 10) }
}

export const isSelectableDate = (iso: string, anyDate: boolean) => {
  if (anyDate) return true
  const { min, max } = selectableRange()
  return iso >= min && iso <= max
}

// Aralık dışındaki tarihi en yakın seçilebilir güne çeker (yetkisi olana dokunmaz).
export const clampMatchDate = (iso: string, anyDate: boolean) => {
  if (anyDate) return iso
  const { min, max } = selectableRange()
  return iso < min ? min : iso > max ? max : iso
}