import { describe, expect, it, vi } from 'vitest'
import { registrarServiceWorker } from './registrarServiceWorker'

function falso() {
  const register = vi.fn(async () => ({}))
  const escuchas: Record<string, () => void> = {}
  const nav = { serviceWorker: { register } } as unknown as Navigator
  const ventana = { addEventListener: (tipo: string, fn: () => void) => { escuchas[tipo] = fn } } as Pick<Window, 'addEventListener'>
  return { nav, ventana, register, escuchas }
}

describe('registrar el service worker', () => {
  it('en desarrollo no se registra: Vite serviria archivos viejos', () => {
    const f = falso()
    expect(registrarServiceWorker(f.nav, f.ventana, false)).toBe(false)
    expect(f.escuchas.load).toBeUndefined()
  })

  it('en produccion se registra /sw.js, despues de que la pagina cargo', () => {
    const f = falso()
    expect(registrarServiceWorker(f.nav, f.ventana, true)).toBe(true)
    expect(f.register).not.toHaveBeenCalled()
    f.escuchas.load()
    expect(f.register).toHaveBeenCalledWith('/sw.js')
  })

  it('un navegador sin service worker sigue funcionando', () => {
    const f = falso()
    expect(registrarServiceWorker({} as Navigator, f.ventana, true)).toBe(false)
  })
})
