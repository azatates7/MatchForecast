import { Component, ElementRef, computed, effect, output, signal, viewChild } from '@angular/core';
import { forgotPassword, getAuthConfig, login, loginWithGoogle, register, resetPassword } from '../core/api';
import { renderGoogleButton } from '../core/google';

type Mode = 'login' | 'register' | 'forgot' | 'reset';
interface FormState { loading: boolean; error?: string; info?: string }

// E-postadaki bağlantı ?resetToken=... ile açılır. Token okunur ve adres çubuğundan hemen silinir
// (tarayıcı geçmişinde, ekran paylaşımında görünmesin).
const takeResetToken = (): string | null => {
  const url = new URL(window.location.href);
  const token = url.searchParams.get('resetToken');
  if (token) {
    url.searchParams.delete('resetToken');
    history.replaceState(null, '', url);
  }   
  return token;
};

const SUBMIT_LABELS: Record<Mode, [idle: string, busy: string]> = {
  login: ['Giriş yap', 'Giriş yapılıyor…'],
  register: ['Kayıt ol', 'Kaydediliyor…'],
  forgot: ['Bağlantı gönder', 'Gönderiliyor…'],
  reset: ['Şifreyi güncelle', 'Güncelleniyor…'],
};

// Giriş, kayıt, şifremi unuttum ve şifre sıfırlama ekranları. Oturum açılınca "authenticated" yayınlanır.
@Component({
  selector: 'app-auth-panel',
  templateUrl: './auth-panel.html',
})
export class AuthPanel {
  readonly authenticated = output<void>();

  private readonly resetToken = takeResetToken();

  protected readonly mode = signal<Mode>(this.resetToken ? 'reset' : 'login');
  protected readonly userName = signal('');
  protected readonly email = signal('');
  protected readonly displayName = signal('');
  protected readonly password = signal('');
  protected readonly passwordRepeat = signal('');
  protected readonly state = signal<FormState>({ loading: false });
  protected readonly googleClientId = signal<string | null>(null);

  protected readonly submitLabel = computed(() => SUBMIT_LABELS[this.mode()][this.state().loading ? 1 : 0]);

  private readonly googleButton = viewChild<ElementRef<HTMLDivElement>>('googleButton');

  constructor() {
    // Google Client ID ayarlı değilse buton hiç gösterilmez.
    getAuthConfig().then(c => this.googleClientId.set(c.googleClientId)).catch(() => undefined);

    // Buton alanı ekrana geldiğinde (giriş ekranına her dönüşte) Google butonu yeniden çizilir.
    effect(() => {
      const element = this.googleButton()?.nativeElement;
      const clientId = this.googleClientId();
      if (!element || !clientId) return;
      renderGoogleButton(element, clientId, idToken => void this.run(() => loginWithGoogle(idToken), true))
        .catch((e: Error) => this.state.set({ loading: false, error: e.message }));
    });
  }

  protected switchTo(mode: Mode, info?: string) {
    this.mode.set(mode);
    this.password.set('');
    this.passwordRepeat.set('');
    this.state.set({ loading: false, info });
  }

  protected submit(event: Event) {
    event.preventDefault(); // Form submit sayfayı yeniden yüklemesin.

    switch (this.mode()) {
      case 'login':
        void this.run(() => login(this.userName().trim(), this.password()), true);
        break;
      case 'register':
        void this.run(() => register(this.userName().trim(), this.email().trim(), this.password(), this.displayName().trim()), true);
        break;
      case 'forgot':
        void this.run(async () => {
          await forgotPassword(this.email().trim());
          this.switchTo('login', 'Bu e-posta kayıtlıysa şifre sıfırlama bağlantısı gönderildi.');
        });
        break;
      case 'reset':
        if (this.password() !== this.passwordRepeat()) {
          this.state.set({ loading: false, error: 'Şifreler eşleşmiyor.' });
          return;
        }
        void this.run(async () => {
          await resetPassword(this.resetToken ?? '', this.password());
          this.switchTo('login', 'Şifren güncellendi, yeni şifrenle giriş yapabilirsin.');
        });
        break;
    }
  }

  // İşlemi çalıştırır, hata mesajını gösterir. authenticates=true ise başarıda uygulamaya oturumun açıldığını bildirir.
  private async run(action: () => Promise<void>, authenticates = false) {
    this.state.set({ loading: true });
    try {
      await action();
      if (authenticates) {
        this.password.set('');
        this.authenticated.emit();
      } else {
        this.state.update(s => ({ ...s, loading: false }));
      }
    } catch (e) {
      this.state.set({ loading: false, error: (e as Error).message });
    }
  }
}