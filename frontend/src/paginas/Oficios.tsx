import { Link } from 'react-router-dom'
import { personasEnRubro } from '../api/comunidad'
import { useCatalogo, useComunidad } from '../api/useCargado'
import { Icono } from '../marco/Icono'
import { capitalizar, personas } from '../marco/texto'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'

export function Oficios() {
  useTitulo('Oficios')
  useMeta({ descripcion: 'Los oficios que puedes encontrar cerca de ti en ZASS!: corretaje de propiedades, derecho, contabilidad, arquitectura, técnicos y más.' })
  const catalogo = useCatalogo()
  const comunidad = useComunidad()

  return (
    <div className="pagina">
      <section className="pagina-cabeza pagina-cabeza--amarillo">
        <div className="contenedor">
          <p className="sobretitulo">Oficios</p>
          <h1 className="pagina-titulo">Quién sabe de lo tuyo.</h1>
          <p className="bajada">
            Elige un oficio y mira a las personas de todo Chile que lo ejercen.
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
              // Solo con la comunidad ya cargada se sabe que esta vacio. Buscarlo no daria a nadie:
              // la fila invita, en cambio, a quien lo ejerce.
              const vacio = comunidad.datos !== null && n === 0
              return (
                <li key={r.id}>
                  <Link className="oficio" to={vacio ? '/profesionales' : `/buscar?q=${encodeURIComponent(r.nombre)}`}>
                    <h2>{capitalizar(r.nombre)}</h2>
                    <span className="oficio-lado">
                      {/* En dos lineas: en una sola, a 390 px se montaba encima de un oficio de una
                          palabra larga («Arquitectura»), que no puede cortarse. */}
                      {comunidad.datos && (vacio ? <span>Sé el primero<br /> en ofrecerlo</span> : personas(n))}
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
