import { NavLink } from 'react-router-dom'
import { useSesion } from '../sesion/contexto'
import { Icono, type NombreDeIcono } from './Icono'
import { useMenuMovil } from './menuMovil'

/**
 * La barra de abajo del telefono, como en una app: lo mas usado al alcance del pulgar.
 * Solo se ve en pantallas angostas (CSS); en escritorio manda la cabecera.
 *
 *   fuera de la sesion:  Inicio · Buscar · Entrar    · Mas
 *   dentro:              Inicio · Buscar · Mis citas · Mas
 *
 * «Mas» abre el mismo menu que el boton «Menu» de la cabecera (menuMovil.tsx): ahi esta todo
 * lo demas (comunas, oficios, la ficha del prestador, salir). Cuatro pestañas y no cinco: con
 * cinco, a 360 px las etiquetas se cortan.
 */
export function BarraInferior() {
  const { estado } = useSesion()
  const { abierto, alternar } = useMenuMovil()

  const tercera: { a: string; texto: string; icono: NombreDeIcono } = estado === 'dentro'
    ? { a: '/mis-citas', texto: 'Mis citas', icono: 'calendario' }
    : { a: '/entrar', texto: 'Entrar', icono: 'usuario' }

  return (
    <nav className="barra-inferior" aria-label="Accesos rápidos">
      <Pestana a="/" texto="Inicio" icono="casa" />
      <Pestana a="/buscar" texto="Buscar" icono="lupa" />
      {/* Mientras la sesion se carga no se sabe cual va: se guarda el lugar para que nada salte. */}
      {estado === 'cargando' ? <span className="pestana pestana--hueco" aria-hidden="true" /> : <Pestana {...tercera} />}
      <button
        type="button" className="pestana" aria-expanded={abierto} aria-controls="menu-movil"
        onClick={alternar}
      >
        <span className="pestana-icono"><Icono nombre={abierto ? 'cerrar' : 'menu'} /></span>
        <span className="pestana-texto">{abierto ? 'Cerrar' : 'Más'}</span>
      </button>
    </nav>
  )
}

/** React Router no marca «/» como activa en otras rutas: «Inicio» solo se enciende en la portada. */
function Pestana({ a, texto, icono }: { a: string; texto: string; icono: NombreDeIcono }) {
  return (
    <NavLink to={a} className="pestana">
      <span className="pestana-icono"><Icono nombre={icono} /></span>
      <span className="pestana-texto">{texto}</span>
    </NavLink>
  )
}
