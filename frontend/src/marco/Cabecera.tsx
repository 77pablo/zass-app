import { Link, NavLink } from 'react-router-dom'
import { useSesion } from '../sesion/contexto'
import { Icono } from './Icono'
import { useMenuMovil } from './menuMovil'

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

/**
 * Estilo iPhone (Pablo, 18-nov-2026: «saca la parte superior que dice zass»). Ya no hay barra con
 * la marca arriba: cada pantalla abre con su titulo grande (y la barra fina de TituloCompacto.tsx
 * al bajar). Lo que vivia en la barra se reparte como en una app:
 *  - en el telefono, la barra de pestañas de abajo (Inicio · Buscar · Entrar/Mis citas · Mas) y la
 *    hoja «Mas», que sigue viviendo aqui (id="menu-movil") con los mismos enlaces de siempre;
 *  - en escritorio, una barra de navegacion de vidrio SIN la marca, con «Inicio» en el lugar del
 *    logo (el mismo destino: «/»).
 * El sitio conserva su nombre para el lector de pantalla: el <header> se llama «ZASS!» y el
 * <title> de cada pagina termina en «· ZASS!» (useTitulo.ts). La marca visible queda en el pie.
 */
export function Cabecera() {
  const { estado, usuario, salir } = useSesion()
  // El mismo estado que la pestaña «Mas» de la barra inferior (menuMovil.tsx): ella abre la hoja.
  const { abierto } = useMenuMovil()

  const esPrestador = usuario?.roles.includes('prestador') ?? false

  const accion = estado === 'dentro' ? (
    <>
      <NavLink to="/mis-citas" className="nav-enlace">Mis citas</NavLink>
      <NavLink to="/mis-resenas" className="nav-enlace">Mis reseñas</NavLink>
      {esPrestador ? (
        <>
          <NavLink to="/mi-ficha" className="nav-enlace">Mi ficha</NavLink>
          <NavLink to="/mi-agenda" className="nav-enlace">Mi agenda</NavLink>
          <NavLink to="/mis-tarjetas" className="nav-enlace">Mis tarjetas</NavLink>
        </>
      ) : (
        <NavLink to="/ofrecer-servicios" className="nav-enlace">Ofrecer mis servicios</NavLink>
      )}
      <button type="button" className="nav-enlace nav-boton" onClick={() => void salir()}>
        Salir
      </button>
    </>
  ) : estado === 'fuera' ? (
    <NavLink to="/entrar" className="nav-entrar">Entrar</NavLink>
  ) : null

  return (
    <header className="cabecera" aria-label="ZASS!">
      <div className="contenedor cabecera-fila">
        <nav className="nav-escritorio" aria-label="Principal">
          <NavLink to="/" end className="nav-enlace">Inicio</NavLink>
          {ENLACES.map((e) => (
            <NavLink key={e.a} to={e.a} className="nav-enlace">{e.texto}</NavLink>
          ))}
          {accion}
        </nav>
      </div>

      {abierto && (
        <nav id="menu-movil" className="menu-movil" aria-label="Principal">
          {ENLACES.map((e) => (
            <NavLink key={e.a} to={e.a} className="menu-enlace">
              {e.texto}<Icono nombre="chevron" />
            </NavLink>
          ))}
          {estado === 'dentro' && (
            <NavLink to="/mis-citas" className="menu-enlace">
              Mis citas<Icono nombre="chevron" />
            </NavLink>
          )}
          {estado === 'dentro' && (
            <NavLink to="/mis-resenas" className="menu-enlace">
              Mis reseñas<Icono nombre="chevron" />
            </NavLink>
          )}
          {estado === 'dentro' && esPrestador && (
            <NavLink to="/mi-ficha" className="menu-enlace">
              Mi ficha<Icono nombre="chevron" />
            </NavLink>
          )}
          {estado === 'dentro' && esPrestador && (
            <NavLink to="/mi-agenda" className="menu-enlace">
              Mi agenda<Icono nombre="chevron" />
            </NavLink>
          )}
          {estado === 'dentro' && esPrestador && (
            <NavLink to="/mis-tarjetas" className="menu-enlace">
              Mis tarjetas<Icono nombre="chevron" />
            </NavLink>
          )}
          {estado === 'dentro' && !esPrestador && (
            <NavLink to="/ofrecer-servicios" className="menu-enlace">
              Ofrecer mis servicios<Icono nombre="chevron" />
            </NavLink>
          )}
          {estado === 'dentro' && (
            <button type="button" className="menu-enlace" onClick={() => void salir()}>
              Salir<Icono nombre="chevron" />
            </button>
          )}
          {estado === 'fuera' && (
            <NavLink to="/entrar" className="menu-enlace menu-enlace--entrar">
              Entrar<Icono nombre="chevron" />
            </NavLink>
          )}
        </nav>
      )}
    </header>
  )
}
