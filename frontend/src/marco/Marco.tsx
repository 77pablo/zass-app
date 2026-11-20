import { useEffect, useRef } from 'react'
import { Outlet, useLocation, useNavigationType } from 'react-router-dom'
import { BarraInferior } from './BarraInferior'
import { Cabecera } from './Cabecera'
import { ProveedorDeMenuMovil } from './ProveedorDeMenuMovil'
import { Pie } from './Pie'
import { TituloCompacto } from './TituloCompacto'

/** Las raices de las pestañas de abajo: entre ellas iOS no empuja, cambia en el sitio. */
const RAICES = new Set(['/', '/buscar', '/entrar', '/mis-citas'])

/**
 * Lo que rodea a toda pagina. Al cambiar de pagina el navegador, en una aplicacion de una
 * sola carga, deja el scroll donde estaba: entrar a una ficha desde la mitad de una lista
 * te dejaria viendo el pie. Se sube al inicio, salvo que la URL traiga un ancla (#algo).
 */
export function Marco() {
  const { pathname, hash } = useLocation()
  const tipo = useNavigationType()
  const principal = useRef<HTMLElement>(null)
  // La ruta de la ultima animacion: en la primera carga (y en el doble efecto del modo estricto)
  // es la misma, y no se anima.
  const anterior = useRef(pathname)

  useEffect(() => {
    if (hash === '') window.scrollTo(0, 0)
  }, [pathname, hash])

  // La transicion entre pantallas, como en el iPhone: ir a otra pantalla la EMPUJA desde la
  // derecha y volver atras (el boton del navegador o el gesto) la trae desde la izquierda; entre
  // las raices de las pestañas, un fundido corto. Solo transform y opacity, con la Web Animations
  // API (corre en el compositor y no necesita libreria). Nunca en la primera carga, y con
  // «reducir movimiento» queda solo el fundido.
  useEffect(() => {
    if (anterior.current === pathname) return
    anterior.current = pathname
    const el = principal.current
    if (el === null || typeof el.animate !== 'function') return
    const quieto = typeof window.matchMedia === 'function'
      && window.matchMedia('(prefers-reduced-motion: reduce)').matches
    const fundido = quieto || RAICES.has(pathname)
    const desde = fundido ? 'none' : tipo === 'POP' ? 'translateX(-6%)' : 'translateX(8%)'
    const animacion = el.animate(
      [{ opacity: 0, transform: desde }, { opacity: 1, transform: 'none' }],
      { duration: fundido ? 160 : 320, easing: 'cubic-bezier(0.32, 0.72, 0, 1)' },
    )
    return () => animacion.cancel()
  }, [pathname, tipo])

  return (
    <ProveedorDeMenuMovil>
      <a className="saltar" href="#contenido">Saltar al contenido</a>
      {/* La capsula de pestañas va ANTES del contenido, y la cabecera (que guarda la hoja «Mas»)
          justo despues de ella: asi el orden del teclado es el que se ve en escritorio (la capsula
          arriba) y el Tab que sigue a «Mas» entra en la hoja. Las dos son position: fixed, el
          orden del DOM no mueve nada en pantalla. */}
      <BarraInferior />
      <Cabecera />
      <TituloCompacto />
      <main id="contenido" tabIndex={-1} ref={principal}>
        <Outlet />
      </main>
      <Pie />
    </ProveedorDeMenuMovil>
  )
}
