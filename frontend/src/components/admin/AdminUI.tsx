import { useState } from 'react'

/*
 * Yönetim sekmelerinin ortak küçük parçaları (Kullanıcılar, Roller, Gruplar, Yetkiler).
 * Angular karşılığı: frontend-angular/src/app/components/admin/admin-action.ts (+ şablonlardaki çip/durum blokları).
 */

// Rol/grup/yetki adları gibi kısa listeler; boşsa tire gösterilir.
export function Chips({ items }: { items: string[] }) {
  if (items.length === 0) return <span className="admin-muted">—</span>
  return <span className="chips">{items.map(x => <span key={x} className="chip">{x}</span>)}</span>
}

export interface ActionState { busy: boolean; error?: string; info?: string }

// Bir düğmenin işlemi: çalışırken busy, bitince bilgi ya da API hata mesajı (ProblemDetails.detail).
// Her bölüm kendi örneğini kullanır ki bir bölümün mesajı diğerininkini silmesin.
export function useAction() {
  const [state, setState] = useState<ActionState>({ busy: false })
  const run = async (action: () => Promise<unknown>, info?: string) => {
    setState({ busy: true })
    try {
      await action()
      setState({ busy: false, info })
    } catch (e) {
      setState({ busy: false, error: (e as Error).message })
    }
  }
  return { ...state, run }
}

export function ActionStatus({ state }: { state: ActionState }) {
  if (state.error) return <p className="note-error admin-status" role="alert">{state.error}</p>
  if (state.info) return <p className="auth-info admin-status" role="status">{state.info}</p>
  return null
}