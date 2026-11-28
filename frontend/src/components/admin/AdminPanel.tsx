import { useEffect, useRef } from 'react'
import { useI18n } from '../../i18n/useI18n'
import { adminHash, visibleAdminTabs, type AdminTab } from '../../AdminView'
import type { MeResponse } from '../../types'
import UsersTab from './UsersTab'
import RolesTab from './RolesTab'
import GroupsTab from './GroupsTab'
import PermissionsTab from './PermissionsTab'
import UsageTab from './UsageTab'
import AccuracyTab from './AccuracyTab'
import KafkaTab from './KafkaTab'

interface Props {
  me: MeResponse
  /** Adresteki sekme; yoksa veya kullanıcı göremiyorsa ilk görünen sekme açılır */
  tab: AdminTab | null
}

/*
 * Yönetim paneli iskeleti: yetkiye göre filtrelenmiş sekmeler (Kullanıcılar, Roller, Gruplar, Yetkiler, AI kullanımı).
 * Sekmeler bağlantıdır (#admin/<sekme>); tarayıcının geri/ileri düğmeleri sekmeler arasında çalışır.
 * Sekmeler: Kullanıcılar, Roller, Gruplar, Yetkiler (her biri kendi bileşeninde, components/admin/).
 */
export default function AdminPanel({ me, tab }: Props) {
  const { t } = useI18n()
  const tabs = visibleAdminTabs(me)
  const active = tabs.find(x => x.id === tab) ?? tabs[0]
  const navRef = useRef<HTMLElement>(null)

  // Dar ekranda sekmeler sığmayınca çubuk yatay kayar; aktif sekme görünür alana (ortaya) getirilir.
  // Yalnızca çubuğun scrollLeft'i değişir, sayfa dikey kaymaz (scrollIntoView'in aksine).
  useEffect(() => {
    const nav = navRef.current
    const el = nav?.querySelector<HTMLElement>('[aria-selected="true"]')
    if (!nav || !el) return
    const offset = el.getBoundingClientRect().left - nav.getBoundingClientRect().left
    nav.scrollLeft += offset - (nav.clientWidth - el.offsetWidth) / 2
  }, [active?.id])

  return (
    <main className="admin">
      <h2 className="admin-title">{t('admin.title')}</h2>
      <nav ref={navRef} className="admin-tabs" role="tablist" aria-label={t('admin.tabs')}>
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
        {active.id === 'users' ? <UsersTab me={me} />
          : active.id === 'roles' ? <RolesTab />
          : active.id === 'groups' ? <GroupsTab me={me} />
          : active.id === 'usage' ? <UsageTab />
          : active.id === 'accuracy' ? <AccuracyTab />
          : active.id === 'kafka' ? <KafkaTab />
          : <PermissionsTab />}
      </section>
    </main>
  )
}