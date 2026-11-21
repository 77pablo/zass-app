import { Link, Outlet } from 'react-router-dom'
import { puedeEntrarAlPanel } from '../funciones/interno/servicio'
import * as T from '../funciones/interno/textos'
import { Icono } from '../marco/Icono'
import { PaginaCargando } from '../marco/PaginaCargando'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'
import { useSesion } from '../sesion/contexto'
import { NoEncontrada } from './NoEncontrada'

/**
 * JQ-11. La puerta de /interno: solo pasa quien tiene el rol `verificador` o `administrador`.
 *
 * Sin el rol (o sin sesión) la ruta responde EXACTAMENTE como una página que no existe, no como
 * «prohibido» ni mandando a /entrar: no se anuncia que hay un panel. La protección real está en la
 * API (política `Verificador`, 403 en todo /api/interno); esto solo evita mostrar la puerta.
 */
export function PuertaInterna() {
  useMeta({ noindex: true })
  const { estado, usuario } = useSesion()

  // Mientras se sabe quién es, nada que nombre al panel: el mismo «Cargando…» de cualquier pantalla.
  if (estado === 'cargando') return <PaginaCargando titulo="Cargando…" />
  if (estado !== 'dentro' || !puedeEntrarAlPanel(usuario)) return <NoEncontrada />
  return <Outlet />
}

/** /interno: el índice de las herramientas. Crece con JQ-12, JQ-13 y JQ-14. */
export function IndiceInterno() {
  useTitulo(T.TITULO_PANEL)
  return (
    <div className="contenedor ficha-pagina">
      <h1 className="pagina-titulo">{T.TITULO_PANEL}</h1>
      <p className="bloque-texto">{T.INTRO_PANEL}</p>
      <ul className="panel-interno-secciones">
        <li className="bloque">
          <Link className="enlace" to="/interno/verificacion">
            {T.SECCION_VERIFICACION}<Icono nombre="chevron" />
          </Link>
          <p className="bloque-texto">{T.SECCION_VERIFICACION_BAJADA}</p>
        </li>
      </ul>
    </div>
  )
}
