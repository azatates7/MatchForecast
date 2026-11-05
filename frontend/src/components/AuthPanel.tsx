import { useEffect, useRef, useState, type FormEvent } from 'react'
import { forgotPassword, generatePassword, getAuthConfig, login, loginAsGuest, loginWithGoogle, register, resetPassword } from '../api'
import { renderGoogleButton } from './Google'
import LanguageToggle from './LanguageToggle'
import { useI18n } from '../i18n/useI18n'
import type { MessageKey } from '../i18n/messages'

type Mode = 'login' | 'register' | 'forgot' | 'reset'

// "Kopyalandı" bilgisinin ekranda kalma süresi
const COPIED_MSG_MS = 2_000
interface FormState { loading: boolean; error?: string; info?: string }

// E-postadaki bağlantı ?resetToken=... ile açılır. Okuma saf tutulur (StrictMode useState başlatıcısını iki kez çağırır);
// token adres çubuğundan bir effect'te silinir (tarayıcı geçmişinde, ekran paylaşımında görünmesin).
const readResetToken = (): string | null => new URL(window.location.href).searchParams.get('resetToken')

const SUBMIT_LABELS: Record<Mode, [idle: MessageKey, busy: MessageKey]> = {
  login: ['auth.submit.login', 'auth.busy.login'],
  register: ['auth.submit.register', 'auth.busy.register'],
  forgot: ['auth.submit.forgot', 'auth.busy.forgot'],
  reset: ['auth.submit.reset', 'auth.busy.reset'],
}

// Giriş, kayıt, şifremi unuttum ve şifre sıfırlama ekranları. Oturum açılınca onAuthenticated çağrılır.
export default function AuthPanel({ onAuthenticated }: { onAuthenticated: () => void }) {
  const { t, lang } = useI18n()
  const [resetToken] = useState(readResetToken)
  const [mode, setMode] = useState<Mode>(resetToken ? 'reset' : 'login')
  const [userName, setUserName] = useState('')
  const [email, setEmail] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [password, setPassword] = useState('')
  const [passwordRepeat, setPasswordRepeat] = useState('')
  const [state, setState] = useState<FormState>({ loading: false })
  const [googleClientId, setGoogleClientId] = useState<string | null>(null)
  const [guestLoginEnabled, setGuestLoginEnabled] = useState(false)
  // Kayıt ekranındaki şifre oluşturucu: oluşturulan şifre alana yazılır ve görünür yapılır ki kullanıcı kaydedebilsin.
  const [showPassword, setShowPassword] = useState(false)
  const [generating, setGenerating] = useState(false)
  const [copied, setCopied] = useState(false)
  const googleButton = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const url = new URL(window.location.href)
    if (!url.searchParams.has('resetToken')) return
    url.searchParams.delete('resetToken')
    history.replaceState(null, '', url)
  }, [])

  // Google Client ID ayarlı değilse veya misafir kullanıcısı yoksa ilgili buton hiç gösterilmez.
  useEffect(() => {
    getAuthConfig()
      .then(c => {
        setGoogleClientId(c.googleClientId)
        setGuestLoginEnabled(c.guestLoginEnabled)
      })
      .catch(() => undefined)
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

  // Buton alanı ekrana geldiğinde (giriş ekranına her dönüşte) ve dil değişince Google butonu yeniden çizilir.
  // Callback ref'te tutulur ki effect her render'da tekrar çalışmasın.
  const runRef = useRef(run)
  useEffect(() => { runRef.current = run })
  useEffect(() => {
    const element = googleButton.current
    if (mode !== 'login' || !element || !googleClientId) return
    renderGoogleButton(element, googleClientId, idToken => void runRef.current(() => loginWithGoogle(idToken), true))
      .catch((e: Error) => setState({ loading: false, error: e.message }))
  }, [mode, googleClientId, lang])

  const switchTo = (next: Mode, info?: string) => {
    setMode(next)
    setPassword('')
    setPasswordRepeat('')
    setShowPassword(false)
    setState({ loading: false, info })
  }

  const handleGeneratePassword = async () => {
    setGenerating(true)
    try {
      const { password: generated } = await generatePassword()
      setPassword(generated)
      setShowPassword(true)
      setState({ loading: false, info: t('auth.generated') })
    } catch (e) {
      setState({ loading: false, error: (e as Error).message })
    } finally {
      setGenerating(false)
    }
  }

  // Clipboard API yalnızca HTTPS/localhost'ta çalışır; başarısız olursa şifre zaten alanda görünür durumda.
  const handleCopyPassword = async () => {
    try {
      await navigator.clipboard.writeText(password)
      setCopied(true)
      setTimeout(() => setCopied(false), COPIED_MSG_MS)
    } catch {
      setState({ loading: false, error: t('auth.copyFailed') })
    }
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
          switchTo('login', t('auth.resetSent'))
        })
        break
      case 'reset':
        if (password !== passwordRepeat) {
          setState({ loading: false, error: t('auth.mismatch') })
          return
        }
        void run(async () => {
          await resetPassword(resetToken ?? '', password)
          switchTo('login', t('auth.resetDone'))
        })
        break
    }
  }

  return (
    <main className="login">
      <form className="login-card" onSubmit={submit}>
        <div className="login-head">
          <h1 className="brand">{t('brand')}</h1>
          <LanguageToggle />
        </div>

        {mode === 'register' && <p className="auth-intro">{t('auth.intro.register')}</p>}
        {mode === 'forgot' && <p className="auth-intro">{t('auth.intro.forgot')}</p>}
        {mode === 'reset' && <p className="auth-intro">{t('auth.intro.reset')}</p>}

        {(mode === 'login' || mode === 'register') && (
          <label className="login-field">
            <span>{t('auth.userName')}</span>
            <input type="text" autoComplete="username" required value={userName} onChange={e => setUserName(e.target.value)} />
          </label>
        )}

        {(mode === 'register' || mode === 'forgot') && (
          <label className="login-field">
            <span>{t('auth.email')}</span>
            <input type="email" autoComplete="email" required value={email} onChange={e => setEmail(e.target.value)} />
          </label>
        )}

        {mode === 'register' && (
          <label className="login-field">
            <span>{t('auth.displayName')}</span>
            <input type="text" autoComplete="name" value={displayName} onChange={e => setDisplayName(e.target.value)} />
          </label>
        )}

        {mode !== 'forgot' && (
          <label className="login-field">
            <span>{t(mode === 'reset' ? 'auth.newPassword' : 'auth.password')}</span>
            <input
              type={showPassword ? 'text' : 'password'}
              required
              autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
              minLength={mode === 'login' ? undefined : 8}
              value={password}
              onChange={e => setPassword(e.target.value)}
            />
          </label>
        )}

        {mode === 'register' && (
          <div className="password-tools">
            <button type="button" className="link-btn" onClick={handleGeneratePassword} disabled={generating}>
              {t(generating ? 'auth.generating' : 'auth.generate')}
            </button>
            {password && (
              <button type="button" className="link-btn" onClick={() => setShowPassword(v => !v)}>
                {t(showPassword ? 'auth.hide' : 'auth.show')}
              </button>
            )}
            {showPassword && password && (
              <button type="button" className="link-btn" onClick={handleCopyPassword}>{t(copied ? 'auth.copied' : 'auth.copy')}</button>
            )}
          </div>
        )}

        {mode === 'reset' && (
          <label className="login-field">
            <span>{t('auth.newPasswordRepeat')}</span>
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
          {t(SUBMIT_LABELS[mode][state.loading ? 1 : 0])}
        </button>

        {mode === 'login' ? (
          <>
            {(googleClientId || guestLoginEnabled) && <div className="auth-divider">{t('auth.or')}</div>}
            {googleClientId && <div ref={googleButton} className="google-button" />}
            {guestLoginEnabled && (
              <button
                type="button"
                className="cache-btn login-btn"
                onClick={() => void run(loginAsGuest, true)}
                disabled={state.loading}
              >
                {t('auth.guest')}
              </button>
            )}
            <div className="auth-links">
              <button type="button" className="link-btn" onClick={() => switchTo('forgot')}>{t('auth.forgot')}</button>
              <button type="button" className="link-btn" onClick={() => switchTo('register')}>{t('auth.register')}</button>
            </div>
          </>
        ) : (
          <div className="auth-links">
            <button type="button" className="link-btn" onClick={() => switchTo('login')}>{t('auth.back')}</button>
          </div>
        )}
      </form>
    </main>
  )
}