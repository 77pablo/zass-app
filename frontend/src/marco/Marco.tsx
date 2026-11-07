import { useEffect } from 'react'
import { Outlet, useLocation } from 'react-router-dom'
import { Cabecera } from './Cabecera'
import { Pie } from './Pie'

/**
 * Lo que rodea a toda pagina. Al cambiar de pagina el navegador, en una aplicacion de una
 * sola carga, deja el scroll donde estaba: entrar a una ficha desde la mitad de una lista
 * te dejaria viendo el pie. Se sube al inicio, salvo que la URL traiga un ancla (#algo).
 */
export function Marco() {
  const { pathname, hash } = useLocation()

  useEffect(() => {
    if (hash === '') window.scrollTo(0, 0)
  }, [pathname, hash])

  return (
    <>
      <a className="saltar" href="#contenido">Saltar al contenido</a>
      <Cabecera />
      <main id="contenido" tabIndex={-1}>
        <Outlet />
      </main>
      <Pie />
    </>
  )
}
