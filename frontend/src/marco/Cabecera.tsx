import { useState } from 'react'
import { Link, NavLink, useLocation } from 'react-router-dom'
import { useSesion } from '../sesion/contexto'
import { Icono } from './Icono'

const ENLACES = [
  { a: '/buscar', texto: 'Buscar' },
  { a: '/comunas', texto: 'Comunas' },
  { a: '/oficios', texto: 'Oficios' },
  { a: '/profesionales', texto: 'Para profesionales' },
]

export function Logo() {
  return (
    <Link to="/" className="logo" aria-label="ZASS!, ir al inicio">
      ZAS<em>S!</em>
    </Link>
  )
}

export function Cabecera() {
  const { estado, usuario, salir } = useSesion()
  const { pathname } = useLocation()

  // El menu guarda la RUTA en la que se abrio, y esta abierto solo mientras sigas en ella.
  // Asi se cierra al navegar sin un efecto que ponga estado: el efecto llegaria un
  // fotograma tarde y el menu se veria abierto sobre la pagina nueva.
  const [abiertoEn, setAbiertoEn] = useState<string | null>(null)
  const abierto = abiertoEn === pathname

  const esPrestador = usuario?.roles.includes('prestador') ?? false

  const accion = estado === 'dentro' ? (
    <>
      {esPrestador && (
        <NavLink to="/mi-ficha" className="nav-enlace">Mi ficha</NavLink>
      )}
      <button type="button" className="nav-enlace nav-boton" onClick={() => void salir()}>
        Salir
      </button>
    </>
  ) : estado === 'fuera' ? (
    <NavLink to="/entrar" className="nav-entrar">Entrar</NavLink>
  ) : null

  return (
    <header className="cabecera">
      <div className="contenedor cabecera-fila">
        <Logo />

        <nav className="nav-escritorio" aria-label="Principal">
          {ENLACES.map((e) => (
            <NavLink key={e.a} to={e.a} className="nav-enlace">{e.texto}</NavLink>
          ))}
          {accion}
        </nav>

        <button
          type="button" className="menu-boton" aria-expanded={abierto} aria-controls="menu-movil"
          onClick={() => setAbiertoEn(abierto ? null : pathname)}
        >
          <Icono nombre={abierto ? 'cerrar' : 'menu'} />
          <span>{abierto ? 'Cerrar' : 'Menú'}</span>
        </button>
      </div>

      {abierto && (
        <nav id="menu-movil" className="menu-movil" aria-label="Principal">
          {ENLACES.map((e) => (
            <NavLink key={e.a} to={e.a} className="menu-enlace">
              {e.texto}<Icono nombre="flecha" />
            </NavLink>
          ))}
          {estado === 'dentro' && esPrestador && (
            <NavLink to="/mi-ficha" className="menu-enlace">
              Mi ficha<Icono nombre="flecha" />
            </NavLink>
          )}
          {estado === 'dentro' && (
            <button type="button" className="menu-enlace" onClick={() => void salir()}>
              Salir<Icono nombre="flecha" />
            </button>
          )}
          {estado === 'fuera' && (
            <NavLink to="/entrar" className="menu-enlace menu-enlace--entrar">
              Entrar<Icono nombre="flecha" />
            </NavLink>
          )}
        </nav>
      )}
    </header>
  )
}
