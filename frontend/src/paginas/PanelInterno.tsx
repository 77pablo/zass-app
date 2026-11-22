import { useEffect, useState, type ReactNode } from 'react'
import { Link, Outlet } from 'react-router-dom'
import { puedeEntrarAlPanel, puedeUsar, type SeccionInterna } from '../funciones/interno/servicio'
import * as T from '../funciones/interno/textos'
import { Icono } from '../marco/Icono'
import { PaginaCargando } from '../marco/PaginaCargando'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'
import { useSesion } from '../sesion/contexto'
import { estadoMfa } from '../funciones/identidad/servicio'
import { NoEncontrada } from './NoEncontrada'

/**
 * JQ-11. La puerta de /interno: solo pasa quien tiene un rol interno (`verificador`, `moderador` o
 * `administrador`, JQ-12). Cada sección tiene además su propia puerta (`SoloSeccion`).
 *
 * Sin el rol (o sin sesión) la ruta responde EXACTAMENTE como una página que no existe, no como
 * «prohibido» ni mandando a /entrar: no se anuncia que hay un panel. La protección real está en la
 * API (política `Verificador`, 403 en todo /api/interno); esto solo evita mostrar la puerta.
 */
export function PuertaInterna() {
  useMeta({ noindex: true })
  const { estado, usuario } = useSesion()
  const puede = estado === 'dentro' && puedeEntrarAlPanel(usuario)
  // MFA obligatorio en el panel (22-nov-2026): la API responde 403 `mfa_requerido` a toda la
  // sección. Aquí se mira antes, para mandar a activarlo en vez de mostrar pantallas que fallan.
  const [mfaActivo, setMfaActivo] = useState<boolean>()

  useEffect(() => {
    if (!puede) return
    let vigente = true
    estadoMfa().then((e) => { if (vigente) setMfaActivo(e.activo) }, () => { if (vigente) setMfaActivo(false) })
    return () => { vigente = false }
  }, [puede])

  // Mientras se sabe quién es, nada que nombre al panel: el mismo «Cargando…» de cualquier pantalla.
  if (estado === 'cargando') return <PaginaCargando titulo="Cargando…" />
  if (!puede) return <NoEncontrada />
  // Mientras se pregunta, el panel se pinta: la protección real es la API, esto solo orienta.
  if (mfaActivo === false) {
    return (
      <div className="contenedor ficha-pagina">
        <h1 className="pagina-titulo">{T.TITULO_PANEL}</h1>
        <p className="bloque-texto">
          Para usar el panel interno primero tienes que activar la verificación en dos pasos en tu cuenta.
        </p>
        <Link className="boton" to="/mi-cuenta/seguridad">Activar la verificación en dos pasos</Link>
      </div>
    )
  }
  return <Outlet />
}

/**
 * JQ-12. La puerta de cada sección: solo quien tiene el rol que su API exige. Sin él, la misma
 * página que no existe: quien verifica no se entera de que hay una sección de moderación.
 */
export function SoloSeccion({ seccion, children }: { seccion: SeccionInterna; children: ReactNode }) {
  const { usuario } = useSesion()
  return puedeUsar(usuario, seccion) ? children : <NoEncontrada />
}

const SECCIONES: { seccion: SeccionInterna; a: string; titulo: string; bajada: string }[] = [
  { seccion: 'verificacion', a: '/interno/verificacion', titulo: T.SECCION_VERIFICACION, bajada: T.SECCION_VERIFICACION_BAJADA },
  { seccion: 'moderacion', a: '/interno/reportes', titulo: T.SECCION_MODERACION, bajada: T.SECCION_MODERACION_BAJADA },
  { seccion: 'administracion', a: '/interno/indicadores', titulo: T.SECCION_INDICADORES, bajada: T.SECCION_INDICADORES_BAJADA },
  { seccion: 'administracion', a: '/interno/tarjetas', titulo: T.SECCION_TARJETAS, bajada: T.SECCION_TARJETAS_BAJADA },
  { seccion: 'administracion', a: '/interno/auditoria', titulo: T.SECCION_AUDITORIA, bajada: T.SECCION_AUDITORIA_BAJADA },
]

/** /interno: el índice, con solo las secciones que tu cuenta puede usar. Crece con JQ-14. */
export function IndiceInterno() {
  useTitulo(T.TITULO_PANEL)
  const { usuario } = useSesion()
  const mias = SECCIONES.filter((s) => puedeUsar(usuario, s.seccion))
  return (
    <div className="contenedor ficha-pagina">
      <h1 className="pagina-titulo">{T.TITULO_PANEL}</h1>
      <p className="bloque-texto">{T.INTRO_PANEL}</p>
      {mias.length === 0 && <p className="bloque-texto bloque-vacio">{T.SIN_SECCIONES}</p>}
      <ul className="panel-interno-secciones">
        {mias.map((s) => (
          <li key={s.a} className="bloque">
            <Link className="enlace" to={s.a}>{s.titulo}<Icono nombre="chevron" /></Link>
            <p className="bloque-texto">{s.bajada}</p>
          </li>
        ))}
      </ul>
    </div>
  )
}
