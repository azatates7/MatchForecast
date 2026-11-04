import { useI18n } from '../i18n/useI18n'
import { adminHash, visibleAdminTabs, type AdminTab } from '../adminView'
import type { MeResponse } from '../types'
import UsersTab from './admin/UsersTab'

interface Props {
  me: MeResponse
  /** Adresteki sekme; yoksa veya kullanıcı göremiyorsa ilk görünen sekme açılır */
  tab: AdminTab | null
}

/*
 * Yönetim paneli iskeleti: yetkiye göre filtrelenmiş sekmeler (Kullanıcılar, Roller, Gruplar, Yetkiler).
 * Sekmeler bağlantıdır (#admin/<sekme>); tarayıcının geri/ileri düğmeleri sekmeler arasında çalışır.
 * Kullanıcılar sekmesi hazır (8.4); Roller, Gruplar ve Yetkiler sonraki adımlarda (8.5–8.7) eklenecek.
 */
export default function AdminPanel({ me, tab }: Props) {
  const { t } = useI18n()
  const tabs = visibleAdminTabs(me)
  const active = tabs.find(x => x.id === tab) ?? tabs[0]

  return (
    <main className="admin">
      <h2 className="admin-title">{t('admin.title')}</h2>
      <nav className="admin-tabs" role="tablist" aria-label={t('admin.tabs')}>
        {tabs.map(x => (
          <a
            key={x.id}
            id={`admin-tab-${x.id}`}
            className="admin-tab"
            href={adminHash(x.id)}
            role="tab"
            aria-selected={x.id === active.id}
            aria-controls="admin-tabpanel"
          >
            {t(x.label)}
          </a>
        ))}
      </nav>
      <section id="admin-tabpanel" className="admin-tabpanel" role="tabpanel" aria-labelledby={`admin-tab-${active.id}`}>
        {active.id === 'users' ? <UsersTab me={me} /> : <p className="admin-placeholder">{t('admin.comingSoon')}</p>}
      </section>
    </main>
  )
}