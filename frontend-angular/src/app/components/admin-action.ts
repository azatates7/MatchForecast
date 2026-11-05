import type { WritableSignal } from '@angular/core';

/*
 * Yönetim sekmelerinin ortak işlem durumu (Kullanıcılar, Roller, Gruplar, Yetkiler).
 * React karşılığı: frontend/src/components/admin/adminUi.tsx (useAction / ActionStatus).
 * Şablonlarda durum satırı: error → .note-error.admin-status, info → .auth-info.admin-status.
 */
export interface ActionState { busy: boolean; error?: string; info?: string }

export const idle = (): ActionState => ({ busy: false });

// Bir düğmenin işlemi: çalışırken busy, bitince bilgi ya da API hata mesajı (ProblemDetails.detail).
// Her bölüm kendi sinyalini kullanır ki bir bölümün mesajı diğerininkini silmesin.
export async function runAction(state: WritableSignal<ActionState>, action: () => Promise<unknown>, info?: string) {
  state.set({ busy: true });
  try {
    await action();
    state.set({ busy: false, info });
  } catch (e) {
    state.set({ busy: false, error: (e as Error).message });
  }
}