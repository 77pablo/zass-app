import { Link } from 'react-router-dom'
import { Logo } from './Cabecera'

export function Pie() {
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
              <li><Link to="/entrar">Entrar</Link></li>
              <li><Link to="/crear-cuenta">Crear una cuenta</Link></li>
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
