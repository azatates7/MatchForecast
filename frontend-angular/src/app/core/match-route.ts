/*
 * Maç adresi: #match/<fikstürId>. Bağlantı paylaşılabilir; tarayıcının geri/ileri tuşu seçilen maçlar arasında gezer.
 * Router kullanılmaz; App hashchange'i dinler (#admin ile aynı yapı). Maçı görme yetkisini API denetler
 * (ör. misafir yalnızca bugünün Süper Lig maçlarını açabilir), burası yalnızca adresi okur/yazar.
 * Not: frontend/src/MatchRoute.ts ile bu dosya birbirinin kopyasıdır, birlikte güncelleyin.
 */
export const matchHash = (id: number) => `#match/${id}`;

// "#match/1557485" → 1557485; başka her şey (boş, #admin, bozuk sayı) → null
export const parseMatchHash = (hash: string): number | null => {
  const m = /^#match\/(\d{1,10})$/.exec(hash);
  if (!m) return null;
  const id = Number(m[1]);
  return id > 0 && id <= 2_147_483_647 ? id : null;
};

// Paylaşılacak tam adres (ör. https://matchforecast.online/#match/1557485)
export const matchUrl = (id: number) => `${location.origin}${location.pathname}${matchHash(id)}`;

// Adresi geçmişe yeni kayıt eklemeden değiştirir (otomatik seçimde geri tuşu boş ekrana dönmesin). hashchange tetiklenmez.
export const replaceHash = (hash: string) => history.replaceState(null, '', hash);

// Bir anın İstanbul takvimindeki günü (YYYY-MM-DD); API ve fikstür tarihleri bu saat dilimindedir.
export const istanbulDate = (d: Date) => {
  const parts = new Intl.DateTimeFormat('en-US', { timeZone: 'Europe/Istanbul', year: 'numeric', month: '2-digit', day: '2-digit' })
    .formatToParts(d);
  const get = (type: string) => parts.find(p => p.type === type)?.value ?? '';
  return `${get('year')}-${get('month')}-${get('day')}`;
};