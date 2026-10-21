/*
 * Google Identity Services (GIS) butonu. Kullanıcı Google ile giriş yapınca GIS bir ID token ("credential") verir;
 * bu token backend'e (/api/auth/google) gönderilir ve orada Google'ın imza anahtarlarıyla doğrulanır.
 * Script yalnızca Google girişi açıksa (Client ID ayarlıysa) ve ilk kez gerektiğinde yüklenir.
 */
interface GoogleAccountsId {
  initialize(options: { client_id: string; callback: (response: { credential: string }) => void }): void
  renderButton(parent: HTMLElement, options: Record<string, unknown>): void
}

declare global {
  interface Window { google?: { accounts: { id: GoogleAccountsId } } }
}

let scriptLoading: Promise<void> | null = null

function loadScript(): Promise<void> {
  if (!scriptLoading) {
    scriptLoading = new Promise<void>((resolve, reject) => {
      const script = document.createElement('script')
      script.src = 'https://accounts.google.com/gsi/client'
      script.async = true
      script.onload = () => resolve()
      script.onerror = () => { scriptLoading = null; reject(new Error('Google girişi yüklenemedi.')) }
      document.head.appendChild(script)
    })
  }
  return scriptLoading
}

export async function renderGoogleButton(parent: HTMLElement, clientId: string, onCredential: (idToken: string) => void) {
  await loadScript()
  const id = window.google?.accounts.id
  if (!id) throw new Error('Google girişi yüklenemedi.')
  id.initialize({ client_id: clientId, callback: response => onCredential(response.credential) })
  parent.replaceChildren()
  id.renderButton(parent, { theme: 'outline', size: 'large', text: 'continue_with', locale: 'tr', width: 280 })
}