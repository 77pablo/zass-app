import { Link } from 'react-router-dom'
import { useSesion } from '../sesion/contexto'
import { Logo } from './Cabecera'

/**
 * El pie del sitio. La columna de «Cuenta» cambia con la sesion: ofrecerle «Entrar» y «Crear una
 * cuenta» a quien ya entro le dice que no lo reconocemos, y encima en la misma pantalla donde la
 * cabecera le saluda por su nombre. Mientras el estado es 'cargando' no se muestra ninguna de las
 * dos: un parpadeo de «Entrar» que luego desaparece da la misma impresion de algo roto.
 */
export function Pie() {
  const { estado, usuario } = useSesion()
  const esPrestador = usuario?.roles.includes('prestador') ?? false

  return (
    <footer className="pie-sitio">
      <div className="contenedor">
        <div className="pie-fila">
          <div>
            <Logo />
            <p>Servicios de tu zona, a un toque. Hecho en Chile.</p>
          </div>
          <nav aria-label="Secciones">
            <ul>
              <li><Link to="/buscar">Buscar a alguien</Link></li>
              <li><Link to="/comunas">Comunas</Link></li>
              <li><Link to="/oficios">Oficios</Link></li>
            </ul>
          </nav>
          <nav aria-label="Cuenta">
            <ul>
              <li><Link to="/profesionales">Para profesionales</Link></li>
              <li><Link to="/planes">Planes y precios</Link></li>
              {estado === 'dentro' && (
                <>
                  <li><Link to="/mis-citas">Mis citas</Link></li>
                  {esPrestador
                    ? <li><Link to="/mi-ficha">Mi ficha</Link></li>
                    : <li><Link to="/ofrecer-servicios">Ofrecer mis servicios</Link></li>}
                </>
              )}
              {estado === 'fuera' && (
                <>
                  <li><Link to="/entrar">Entrar</Link></li>
                  <li><Link to="/crear-cuenta">Crear una cuenta</Link></li>
                </>
              )}
              <li><Link to="/terminos">Términos de uso</Link></li>
              <li><Link to="/privacidad">Privacidad</Link></li>
            </ul>
          </nav>
        </div>
        <p className="pie-final">
          Buscar es gratis y lo seguirá siendo. Zonas con datos de{' '}
          <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noreferrer noopener">
            OpenStreetMap contributors
          </a>.
        </p>
      </div>
    </footer>
  )
}
