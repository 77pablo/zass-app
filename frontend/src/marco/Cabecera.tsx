import { NavLink } from 'react-router-dom'
import { ENLACE_MENU as ENLACE_ACTIVIDAD } from '../funciones/metricas/textos'
import { puedeEntrarAlPanel } from '../funciones/interno/servicio'
import { ENLACE_MENU as ENLACE_PANEL_INTERNO } from '../funciones/interno/textos'
import { useSesion } from '../sesion/contexto'
import { Icono } from './Icono'
import { useMenuMovil } from './menuMovil'

const ENLACES = [
  { a: '/buscar', texto: 'Buscar' },
  { a: '/comunas', texto: 'Comunas' },
  { a: '/oficios', texto: 'Oficios' },
  { a: '/profesionales', texto: 'Para profesionales' },
]

/**
 * Estilo iPhone (Pablo, 18 y 19-nov-2026). Ya no hay barra con la marca arriba: cada pantalla abre
 * con su titulo grande (y la barra fina de TituloCompacto.tsx al bajar). La navegacion es UNA sola,
 * la de pestañas (BarraInferior.tsx: Inicio · Buscar · Entrar/Mis citas · Mas), que en el telefono
 * flota abajo y en escritorio flota arriba al centro, como en el iPad. Todo lo demas vive en la
 * hoja «Mas», aqui (id="menu-movil"): en el telefono sube desde abajo; en escritorio cae como un
 * popover bajo la capsula. Mismos enlaces y mismos destinos que antes.
 * El sitio conserva su nombre para el lector de pantalla: el <header> se llama «ZASS!» y el
 * <title> de cada pagina termina en «· ZASS!» (useTitulo.ts).
 */
export function Cabecera() {
  const { estado, usuario, salir } = useSesion()
  // El mismo estado que la pestaña «Mas» de la barra de pestañas (menuMovil.tsx): ella abre la hoja.
  const { abierto } = useMenuMovil()

  const esPrestador = usuario?.roles.includes('prestador') ?? false
  // JQ-11: el enlace al panel solo existe para quien puede entrar; para el resto, no hay panel.
  const vePanel = estado === 'dentro' && puedeEntrarAlPanel(usuario)

  return (
    <header className="cabecera" aria-label="ZASS!">
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
            <NavLink to="/planes" className="menu-enlace">
              Mi plan<Icono nombre="chevron" />
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
          {estado === 'dentro' && esPrestador && (
            <NavLink to="/mi-actividad" className="menu-enlace">
              {ENLACE_ACTIVIDAD}<Icono nombre="chevron" />
            </NavLink>
          )}
          {estado === 'dentro' && !esPrestador && (
            <NavLink to="/ofrecer-servicios" className="menu-enlace">
              Ofrecer mis servicios<Icono nombre="chevron" />
            </NavLink>
          )}
          {estado === 'dentro' && (
            <NavLink to="/mi-cuenta/seguridad" className="menu-enlace">
              Seguridad de mi cuenta<Icono nombre="chevron" />
            </NavLink>
          )}
          {vePanel && (
            <NavLink to="/interno" className="menu-enlace">
              {ENLACE_PANEL_INTERNO}<Icono nombre="chevron" />
            </NavLink>
          )}
          {estado === 'dentro' && (
            <NavLink to="/mi-cuenta" className="menu-enlace">
              Mi cuenta<Icono nombre="chevron" />
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
