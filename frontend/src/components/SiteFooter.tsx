import { useI18n } from '../i18n/useI18n'

/*
 * Sitenin ortak alt bilgisi: her ekranda aynı (giriş, maç seçilmemiş, maç yok, tahmin, yönetim). Tek satır; dar ekranda
 * iki satıra iner. Maça özel bilgi (tahminin neye dayandığı) tahmin panelinin kendi dipnotunda kalır.
 * Angular karşılığı: frontend-angular/src/app/components/site-footer.ts
 */
export default function SiteFooter() {
  const { t } = useI18n()
  return (
    <footer className="site-foot">
      <p>
        <span className="age-badge" title={t('footer.age')}>18+</span>
        <span>{t('footer.notice')}</span>
      </p>
      <p>{t('footer.copyright', { year: new Date().getFullYear() })}</p>
    </footer>
  )
}