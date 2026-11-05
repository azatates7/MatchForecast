import { TestBed } from '@angular/core/testing';
import { AdminPanel } from './admin-panel';
import { canOpenAdmin, parseAdminHash, visibleAdminTabs } from '../core/admin-view';
import type { MeResponse, PermissionResponse, RoleResponse } from '../core/types';

const admin: MeResponse = { userName: 'azat', displayName: null, roles: ['Admin'], permissions: [] };
const permissionManager: MeResponse = { userName: 'ali', displayName: null, roles: ['User'], permissions: ['permissions.manage'] };

const roles: RoleResponse[] = [
  { id: 1, name: 'Admin', description: null, isSystem: true, hasAllPermissions: true, permissionIds: [], userCount: 1, groupCount: 0 },
  { id: 4, name: 'Editor', description: null, isSystem: false, hasAllPermissions: false, permissionIds: [9], userCount: 0, groupCount: 0 },
];
const permissions: PermissionResponse[] = [
  { id: 1, key: 'users.view', name: 'Kullanıcıları görüntüleme', nameEn: 'View users', description: null, isSystem: true, roleIds: [] },
  { id: 9, key: 'reports.view', name: 'Raporları görüntüleme', nameEn: 'View reports', description: null, isSystem: false, roleIds: [4] },
];

// Ekran bir sonraki karede güncellenir (zoneless); koşul sağlanana kadar kısa aralıklarla bekle.
async function eventually(check: () => boolean) {
  for (let i = 0; i < 50 && !check(); i++) await new Promise(r => setTimeout(r, 20));
  expect(check()).toBe(true);
}

describe('admin-view', () => {
  it('parses the admin hash', () => {
    expect(parseAdminHash('#admin')).toEqual({ admin: true, tab: null });
    expect(parseAdminHash('#admin/roles')).toEqual({ admin: true, tab: 'roles' });
    expect(parseAdminHash('#admin/bilinmeyen')).toEqual({ admin: true, tab: null });
    expect(parseAdminHash('#maclar')).toEqual({ admin: false, tab: null });
  });

  it('shows tabs by permission', () => {
    expect(visibleAdminTabs(admin).map(x => x.id)).toEqual(['users', 'roles', 'groups', 'permissions']);
    expect(visibleAdminTabs(permissionManager).map(x => x.id)).toEqual(['permissions']);
    expect(canOpenAdmin({ ...permissionManager, permissions: ['cache.clear'] })).toBe(false);
    expect(canOpenAdmin(null)).toBe(false);
  });
});

describe('AdminPanel', () => {
  beforeEach(async () => {
    // Oturum: refresh token var, API yalnızca bu testin ihtiyaç duyduğu uçları döner.
    localStorage.setItem('matchforecast.refreshToken', 'test');
    globalThis.fetch = (async (url: string) => {
      const u = String(url);
      const body = u.includes('/api/auth/refresh') ? { accessToken: 't', refreshToken: 'test' }
        : u.includes('/api/permissions') ? permissions
        : u.includes('/api/roles') ? roles
        : [];
      return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } });
    }) as typeof fetch;

    await TestBed.configureTestingModule({ imports: [AdminPanel] }).compileComponents();
  });

  afterEach(() => localStorage.removeItem('matchforecast.refreshToken'));

  it('renders all tabs for Admin and opens the permissions tab', async () => {
    const fixture = TestBed.createComponent(AdminPanel);
    fixture.componentRef.setInput('me', admin);
    fixture.componentRef.setInput('tab', 'permissions');
    const el = fixture.nativeElement as HTMLElement;

    await eventually(() => el.querySelectorAll('.admin-table tbody tr').length === 2);
    expect(el.querySelectorAll('[role="tab"]').length).toBe(4);
    expect(el.querySelector('[aria-selected="true"]')?.id).toBe('admin-tab-permissions');
    // Anahtara göre sıralı; özel yetkinin rolü ada çevrilmiş
    const rows = [...el.querySelectorAll('.admin-table tbody tr')];
    expect(rows.map(r => r.querySelector('code')?.textContent)).toEqual(['reports.view', 'users.view']);
    expect(rows[0].querySelector('.chip')?.textContent).toBe('Editor');
  });

  it('shows only the permissions tab to a permissions.manage user', async () => {
    const fixture = TestBed.createComponent(AdminPanel);
    fixture.componentRef.setInput('me', permissionManager);
    fixture.componentRef.setInput('tab', 'users'); // göremediği sekme istenirse ilk görünen açılır
    const el = fixture.nativeElement as HTMLElement;

    await eventually(() => el.querySelectorAll('.admin-table tbody tr').length === 2);
    expect([...el.querySelectorAll('[role="tab"]')].map(x => x.id)).toEqual(['admin-tab-permissions']);
  });
});