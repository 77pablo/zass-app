import { Link } from 'react-router-dom'
import { personasEnRubro } from '../api/comunidad'
import { useCatalogo, useComunidad } from '../api/useCargado'
import { Icono } from '../marco/Icono'
import { capitalizar, personas } from '../marco/texto'
import { useTitulo } from '../marco/useTitulo'

export function Oficios() {
  useTitulo('Oficios')
  const catalogo = useCatalogo()
  const comunidad = useComunidad()

  return (
    <div className="pagina">
      <section className="pagina-cabeza pagina-cabeza--amarillo">
        <div className="contenedor">
          <p className="sobretitulo">Oficios</p>
          <h1 className="pagina-titulo">Quién sabe de lo tuyo.</h1>
          <p className="bajada">
            Elige un oficio y mira a las personas de Temuco que lo ejercen.
          </p>
        </div>
      </section>

      <div className="seccion"><div className="contenedor">
        {catalogo.error && (
          <p className="aviso-linea" role="alert">
            {catalogo.error}{' '}
            <button type="button" className="enlace" onClick={catalogo.reintentar}>Reintentar</button>
          </p>
        )}
        {catalogo.cargando && <p className="cargando-linea" role="status">Cargando los oficios…</p>}

        {catalogo.datos && (
          <ul className="oficios">
            {catalogo.datos.rubros.map((r) => {
              const n = personasEnRubro(comunidad.datos, r.id)
              return (
                <li key={r.id}>
                  <Link className="oficio" to={`/buscar?rubro=${encodeURIComponent(r.codigo)}`}>
                    <h2>{capitalizar(r.nombre)}</h2>
                    <span className="oficio-lado">
                      {comunidad.datos && (n === 0 ? 'Aún nadie' : personas(n))}
                      <Icono nombre="flecha" />
                    </span>
                  </Link>
                </li>
              )
            })}
          </ul>
        )}
      </div></div>
    </div>
  )
}
