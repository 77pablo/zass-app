import { useEffect } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { Icono } from '../marco/Icono'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'
import { NoEncontrada } from './NoEncontrada'
import { TEXTOS_DE_TARJETA, type Aviso } from './textosDeTarjeta'

/** /tarjeta/no-valida, /tarjeta/no-disponible y /tarjeta/sin-activar: a donde lleva API-54 cuando no hay ficha. */
export function AvisoDeTarjeta({ aviso }: { aviso: Aviso }) {
  const { pestana, titulo, texto } = TEXTOS_DE_TARJETA[aviso]
  useTitulo(pestana)
  useMeta({ noindex: true })
  return (
    <section className="contenedor no-encontrada">
      <p className="sobretitulo sobretitulo--tinta">Tarjeta ZASS!</p>
      <h1 className="pagina-titulo">{titulo}</h1>
      <p className="bajada">{texto}</p>
      <p className="seccion-cierre">
        <Link className="boton boton--amarillo" to="/buscar">Buscar a alguien<Icono nombre="flecha" /></Link>
        <Link className="boton" to="/">Ir al inicio</Link>
      </p>
    </section>
  )
}

/**
 * /T/... : hay generadores de QR que pasan la URL entera a mayusculas. Esa ruta no tiene Function
 * (solo /t/), asi que cae en la app, que la manda a /t/ con la consulta intacta.
 *
 * SOLO con "/T/" exacto. Si a la app llega un "/t/" en minuscula es que falta la Function o el proxy:
 * redirigir ahi seria un ciclo, asi que se muestra "no encontrada".
 */
export function TarjetaEnMayusculas({
  navegar = (url: string) => window.location.replace(url),
}: { navegar?: (url: string) => void }) {
  const { pathname, search } = useLocation()
  const enMayusculas = pathname.startsWith('/T/')
  useEffect(() => {
    if (enMayusculas) navegar(`/t/${pathname.slice(3)}${search}`)
  }, [enMayusculas, navegar, pathname, search])
  return enMayusculas ? null : <NoEncontrada />
}
