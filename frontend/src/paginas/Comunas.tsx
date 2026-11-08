import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { personasEnRegion, personasEnZona } from '../api/comunidad'
import { useCatalogo, useComunidad } from '../api/useCargado'
import { Icono } from '../marco/Icono'
import { personas, sinTildes } from '../marco/texto'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'
import { agruparPorRegion } from '../marco/zonas'

/**
 * Todo Chile, comuna por comuna, agrupado por region de norte a sur. Se puede filtrar escribiendo (sin
 * importar tildes ni mayusculas), y las regiones que ya tienen gente se abren solas: el resto, cerradas, para que
 * una lista de 346 comunas no sea un muro.
 */
export function Comunas() {
  useTitulo('Comunas')
  useMeta({ descripcion: 'Encuentra a gente de tu comuna: cuantos profesionales y oficios hay en cada una de las 346 comunas de Chile. Buscar en ZASS! es gratis.' })
  const catalogo = useCatalogo()
  const comunidad = useComunidad()
  const [filtro, setFiltro] = useState('')

  const buscado = sinTildes(filtro.trim())
  const grupos = useMemo(() => agruparPorRegion(catalogo.datos?.zonas ?? []), [catalogo.datos])

  const visibles = useMemo(() => {
    if (buscado === '') return grupos
    return grupos
      .map((g) => ({
        ...g,
        // Una comuna entra si su nombre coincide o si coincide alguno de sus barrios.
        comunas: g.comunas.filter((c) =>
          sinTildes(c.comuna.nombre).includes(buscado) || c.barrios.some((b) => sinTildes(b.nombre).includes(buscado))),
      }))
      .filter((g) => g.comunas.length > 0)
  }, [grupos, buscado])

  const totalComunas = grupos.reduce((n, g) => n + g.comunas.length, 0)

  return (
    <div className="pagina">
      <section className="pagina-cabeza pagina-cabeza--amarillo">
        <div className="contenedor">
          <p className="sobretitulo">Todo Chile</p>
          <h1 className="pagina-titulo">Tu comuna, con nombre y apellido.</h1>
          <p className="bajada">
            Elige dónde vives y mira quién atiende ahí. Los números son personas con ficha publicada hoy.
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
        {catalogo.cargando && <p className="cargando-linea" role="status"><span className="pulso" aria-hidden="true" />Cargando las comunas…</p>}

        {catalogo.datos && (
          <>
            <div className="filtro-comunas">
              <label htmlFor="filtro-comuna" className="solo-lector">Buscar una comuna</label>
              <Icono nombre="lupa" />
              <input
                id="filtro-comuna" type="search" autoComplete="off" spellCheck={false} maxLength={60}
                placeholder={`Busca tu comuna entre las ${totalComunas}…`}
                value={filtro} onChange={(e) => setFiltro(e.target.value)}
              />
            </div>

            {visibles.length === 0 && (
              <p className="vacio-linea" role="status">No hay ninguna comuna que se llame así. Revisa cómo la escribiste.</p>
            )}

            <div className="regiones">
              {visibles.map((g) => {
                const enRegion = personasEnRegion(comunidad.datos, g.region)
                return (
                  <details key={g.region} className="region" open={buscado !== '' || enRegion > 0}>
                    <summary>
                      <span className="region-nombre">{g.region}</span>
                      <span className="region-cuenta">
                        {comunidad.datos ? (enRegion === 0 ? 'Aún nadie' : personas(enRegion)) : ''}
                        <i aria-hidden="true" />
                      </span>
                    </summary>
                    <ul className="comunas-lista">
                      {g.comunas.map(({ comuna, barrios }) => {
                        const n = personasEnZona(comunidad.datos, comuna.id)
                        return (
                          <li key={comuna.id}>
                            <Link className="comuna" to={`/buscar?zona=${comuna.id}`}>
                              {comuna.nombre}
                              {comunidad.datos && (
                                <small className={n === 0 ? 'cuenta cuenta--cero' : 'cuenta'}>
                                  {n === 0 ? 'Aún nadie' : personas(n)}
                                </small>
                              )}
                            </Link>
                            {barrios.length > 0 && (
                              <ul className="barrios-lista" aria-label={`Barrios de ${comuna.nombre}`}>
                                {barrios.map((b) => {
                                  const nb = personasEnZona(comunidad.datos, b.id)
                                  return (
                                    <li key={b.id}>
                                      <Link className="barrio-enlace" to={`/buscar?zona=${b.id}`}>
                                        {b.nombre}
                                        {comunidad.datos && (
                                          <small className={nb === 0 ? 'cuenta cuenta--cero' : 'cuenta'}>
                                            {nb === 0 ? 'Aún nadie' : personas(nb)}
                                          </small>
                                        )}
                                      </Link>
                                    </li>
                                  )
                                })}
                              </ul>
                            )}
                          </li>
                        )
                      })}
                    </ul>
                  </details>
                )
              })}
            </div>
          </>
        )}
      </div></div>
    </div>
  )
}
