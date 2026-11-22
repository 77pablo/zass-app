import { useEffect, useRef, useState } from 'react'

/*
 * N3 (auditoría del 22-nov-2026): captcha Cloudflare Turnstile en crear cuenta y en «Quiero mi
 * cupo», para que nadie acapare los 50 cupos fundador con cuentas falsas.
 *
 * La clave PÚBLICA del widget viene de VITE_TURNSTILE_SITE_KEY (variable de Cloudflare Pages,
 * se lee al construir). Sin ella no se muestra nada y no se manda token: el backend, sin su
 * secreto, tampoco lo pide. Así nada se rompe mientras Pablo no cree el widget.
 *
 * El token es de UN solo uso: después de cada envío (salga bien o mal) hay que pedir otro, y
 * para eso está `reinicio`.
 */

interface ApiTurnstile {
  render(caja: HTMLElement, opciones: Record<string, unknown>): string
  reset(id?: string): void
  remove(id?: string): void
}

declare global {
  interface Window { turnstile?: ApiTurnstile }
}

export type AccionCaptcha = 'registro' | 'cupo_fundador'

export const CABECERA_CAPTCHA = 'X-Turnstile-Token'
const SCRIPT = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit'

function claveDelSitio(): string {
  return (import.meta.env.VITE_TURNSTILE_SITE_KEY as string | undefined)?.trim() ?? ''
}

/** ¿Hay que resolver el captcha antes de enviar? Solo si el sitio tiene clave. */
export function captchaActivo(): boolean {
  return claveDelSitio().length > 0
}

/** La cabecera para el backend, o nada si no hay token. */
export function cabeceraCaptcha(token: string | null): Record<string, string> {
  return token ? { [CABECERA_CAPTCHA]: token } : {}
}

let cargaDelScript: Promise<void> | null = null

function cargarScript(): Promise<void> {
  if (window.turnstile) return Promise.resolve()
  cargaDelScript ??= new Promise<void>((resolver, rechazar) => {
    const s = document.createElement('script')
    s.src = SCRIPT
    s.async = true
    s.onload = () => resolver()
    s.onerror = () => {
      cargaDelScript = null
      rechazar(new Error('turnstile'))
    }
    document.head.appendChild(s)
  })
  return cargaDelScript
}

interface Props {
  accion: AccionCaptcha
  /** Token nuevo, o null cuando vence, falla o se reinicia. */
  alResolver: (token: string | null) => void
  /** Súbelo en uno para pedir un token nuevo (después de cada envío). */
  reinicio?: number
}

export function Captcha({ accion, alResolver, reinicio = 0 }: Props) {
  const caja = useRef<HTMLDivElement>(null)
  const widget = useRef<string | null>(null)
  const avisar = useRef(alResolver)
  const [fallo, setFallo] = useState(false)

  useEffect(() => { avisar.current = alResolver }, [alResolver])

  useEffect(() => {
    if (!captchaActivo()) return
    let vivo = true
    cargarScript()
      .then(() => {
        if (!vivo || caja.current === null || !window.turnstile) return
        widget.current = window.turnstile.render(caja.current, {
          sitekey: claveDelSitio(),
          action: accion,
          language: 'es',
          callback: (token: string) => avisar.current(token),
          'expired-callback': () => avisar.current(null),
          'error-callback': () => avisar.current(null),
        })
      })
      .catch(() => { if (vivo) setFallo(true) })
    return () => {
      vivo = false
      if (widget.current !== null) window.turnstile?.remove(widget.current)
      widget.current = null
    }
  }, [accion])

  useEffect(() => {
    if (reinicio === 0 || widget.current === null) return
    window.turnstile?.reset(widget.current)
    avisar.current(null)
  }, [reinicio])

  if (!captchaActivo()) return null
  return (
    <div className="captcha">
      <div ref={caja} />
      {fallo && (
        <p className="campo-error" role="alert">
          No pudimos cargar la verificación de que eres una persona. Revisa tu conexión y
          recarga la página.
        </p>
      )}
    </div>
  )
}
