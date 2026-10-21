import { useEffect, useRef, useState, type FormEvent } from 'react'
import { forgotPassword, getAuthConfig, login, loginWithGoogle, register, resetPassword } from '../api'
import { renderGoogleButton } from './Google'

type Mode = 'login' | 'register' | 'forgot' | 'reset'
interface FormState { loading: boolean; error?: string; info?: string }

// E-postadaki bağlantı ?resetToken=... ile açılır. Okuma saf tutulur (StrictMode useState başlatıcısını iki kez çağırır);
// token adres çubuğundan bir effect'te silinir (tarayıcı geçmişinde, ekran paylaşımında görünmesin).
const readResetToken = (): string | null => new URL(window.location.href).searchParams.get('resetToken')

const SUBMIT_LABELS: Record<Mode, [idle: string, busy: string]> = {
  login: ['Giriş yap', 'Giriş yapılıyor…'],
  register: ['Kayıt ol', 'Kaydediliyor…'],
  forgot: ['Bağlantı gönder', 'Gönderiliyor…'],
  reset: ['Şifreyi güncelle', 'Güncelleniyor…'],
}

// Giriş, kayıt, şifremi unuttum ve şifre sıfırlama ekranları. Oturum açılınca onAuthenticated çağrılır.
export default function AuthPanel({ onAuthenticated }: { onAuthenticated: () => void }) {
  const [resetToken] = useState(readResetToken)
  const [mode, setMode] = useState<Mode>(resetToken ? 'reset' : 'login')
  const [userName, setUserName] = useState('')
  const [email, setEmail] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [password, setPassword] = useState('')
  const [passwordRepeat, setPasswordRepeat] = useState('')
  const [state, setState] = useState<FormState>({ loading: false })
  const [googleClientId, setGoogleClientId] = useState<string | null>(null)
  const googleButton = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const url = new URL(window.location.href)
    if (!url.searchParams.has('resetToken')) return
    url.searchParams.delete('resetToken')
    history.replaceState(null, '', url)
  }, [])

  // Google Client ID ayarlı değilse buton hiç gösterilmez.
  useEffect(() => {
    getAuthConfig().then(c => setGoogleClientId(c.googleClientId)).catch(() => undefined)
  }, [])

  // İşlemi çalıştırır, hata mesajını gösterir. authenticates=true ise başarıda oturumun açıldığını bildirir.
  const run = async (action: () => Promise<void>, authenticates = false) => {
    setState({ loading: true })
    try {
      await action()
      if (authenticates) {
        setPassword('')
        onAuthenticated()
      } else {
        setState(s => ({ ...s, loading: false }))
      }
    } catch (e) {
      setState({ loading: false, error: (e as Error).message })
    }
  }

  // Buton alanı ekrana geldiğinde (giriş ekranına her dönüşte) Google butonu yeniden çizilir.
  // Callback ref'te tutulur ki effect her render'da tekrar çalışmasın.
  const runRef = useRef(run)
  useEffect(() => { runRef.current = run })
  useEffect(() => {
    const element = googleButton.current
    if (mode !== 'login' || !element || !googleClientId) return
    renderGoogleButton(element, googleClientId, idToken => void runRef.current(() => loginWithGoogle(idToken), true))
      .catch((e: Error) => setState({ loading: false, error: e.message }))
  }, [mode, googleClientId])

  const switchTo = (next: Mode, info?: string) => {
    setMode(next)
    setPassword('')
    setPasswordRepeat('')
    setState({ loading: false, info })
  }

  const submit = (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault() // Form submit sayfayı yeniden yüklemesin.

    switch (mode) {
      case 'login':
        void run(() => login(userName.trim(), password), true)
        break
      case 'register':
        void run(() => register(userName.trim(), email.trim(), password, displayName.trim()), true)
        break
      case 'forgot':
        void run(async () => {
          await forgotPassword(email.trim())
          switchTo('login', 'Bu e-posta kayıtlıysa şifre sıfırlama bağlantısı gönderildi.')
        })
        break
      case 'reset':
        if (password !== passwordRepeat) {
          setState({ loading: false, error: 'Şifreler eşleşmiyor.' })
          return
        }
        void run(async () => {
          await resetPassword(resetToken ?? '', password)
          switchTo('login', 'Şifren güncellendi, yeni şifrenle giriş yapabilirsin.')
        })
        break
    }
  }

  return (
    <main className="login">
      <form className="login-card" onSubmit={submit}>
        <h1 className="brand">MatchForecast</h1>

        {mode === 'register' && <p className="auth-intro">Yeni hesap oluştur.</p>}
        {mode === 'forgot' && <p className="auth-intro">Kayıtlı e-posta adresine şifre sıfırlama bağlantısı gönderelim.</p>}
        {mode === 'reset' && <p className="auth-intro">Yeni şifreni belirle.</p>}

        {(mode === 'login' || mode === 'register') && (
          <label className="login-field">
            <span>Kullanıcı adı</span>
            <input type="text" autoComplete="username" required value={userName} onChange={e => setUserName(e.target.value)} />
          </label>
        )}

        {(mode === 'register' || mode === 'forgot') && (
          <label className="login-field">
            <span>E-posta</span>
            <input type="email" autoComplete="email" required value={email} onChange={e => setEmail(e.target.value)} />
          </label>
        )}

        {mode === 'register' && (
          <label className="login-field">
            <span>Ad soyad (isteğe bağlı)</span>
            <input type="text" autoComplete="name" value={displayName} onChange={e => setDisplayName(e.target.value)} />
          </label>
        )}

        {mode !== 'forgot' && (
          <label className="login-field">
            <span>{mode === 'reset' ? 'Yeni şifre' : 'Şifre'}</span>
            <input
              type="password"
              required
              autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
              minLength={mode === 'login' ? undefined : 8}
              value={password}
              onChange={e => setPassword(e.target.value)}
            />
          </label>
        )}

        {mode === 'reset' && (
          <label className="login-field">
            <span>Yeni şifre (tekrar)</span>
            <input
              type="password"
              autoComplete="new-password"
              required
              minLength={8}
              value={passwordRepeat}
              onChange={e => setPasswordRepeat(e.target.value)}
            />
          </label>
        )}

        {state.info && <p className="auth-info" role="status">{state.info}</p>}
        {state.error && <p className="note-error" role="alert">{state.error}</p>}

        <button type="submit" className="cache-btn login-btn" disabled={state.loading}>
          {SUBMIT_LABELS[mode][state.loading ? 1 : 0]}
        </button>

        {mode === 'login' ? (
          <>
            {googleClientId && (
              <>
                <div className="auth-divider">veya</div>
                <div ref={googleButton} className="google-button" />
              </>
            )}
            <div className="auth-links">
              <button type="button" className="link-btn" onClick={() => switchTo('forgot')}>Şifremi unuttum</button>
              <button type="button" className="link-btn" onClick={() => switchTo('register')}>Kayıt ol</button>
            </div>
          </>
        ) : (
          <div className="auth-links">
            <button type="button" className="link-btn" onClick={() => switchTo('login')}>Girişe dön</button>
          </div>
        )}
      </form>
    </main>
  )
}