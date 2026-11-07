import { Link } from 'react-router-dom'
import { personasEnZona } from '../api/comunidad'
import { useCatalogo, useComunidad } from '../api/useCargado'
import type { Zona } from '../api/catalogo'
import { capitalizar, personas } from '../marco/texto'
import { useTitulo } from '../marco/useTitulo'

/** Los barrios agrupados por sector, en el orden en que la API los entrega. */
function porSector(zonas: Zona[]): [string, Zona[]][] {
  const grupos = new Map<string, Zona[]>()
  for (const z of zonas) grupos.set(z.sector, [...(grupos.get(z.sector) ?? []), z])
  return [...grupos.entries()]
}

export function Barrios() {
  useTitulo('Barrios')
  const catalogo = useCatalogo()
  const comunidad = useComunidad()

  return (
    <div className="pagina">
      <section className="pagina-cabeza pagina-cabeza--tinta">
        <div className="contenedor">
          <p className="sobretitulo">Temuco y alrededores</p>
          <h1 className="pagina-titulo">Tu barrio, con nombre y apellido.</h1>
          <p className="bajada">
            Elige donde vives y mira quién atiende ahí. Los números son personas con ficha
            publicada hoy.
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
        {catalogo.cargando && <p className="cargando-linea" role="status">Cargando los barrios…</p>}

        {catalogo.datos && (
          <div className="barrios">
            {porSector(catalogo.datos.zonas).map(([sector, zonas]) => (
              <section key={sector} className="sector">
                <h2>{capitalizar(sector)}</h2>
                <ul>
                  {zonas.map((z) => {
                    const n = personasEnZona(comunidad.datos, z.id)
                    return (
                      <li key={z.id}>
                        <Link className="barrio" to={`/buscar?zona=${z.id}`}>
                          {z.nombre}
                          {comunidad.datos && (
                            <small className={n === 0 ? 'cuenta cuenta--cero' : 'cuenta'}>
                              {n === 0 ? 'Aún nadie' : personas(n)}
                            </small>
                          )}
                        </Link>
                      </li>
                    )
                  })}
                </ul>
              </section>
            ))}
          </div>
        )}
      </div></div>
    </div>
  )
}
