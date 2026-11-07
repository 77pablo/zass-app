import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useCatalogo, useComunidad } from '../api/useCargado'
import { Icono } from '../marco/Icono'
import { Monograma } from '../marco/Monograma'
import { TarjetaPersona } from '../marco/TarjetaPersona'
import { capitalizar, personas } from '../marco/texto'
import { useTitulo } from '../marco/useTitulo'

/**
 * La portada es corta a proposito: dice que es ZASS!, deja buscar en una frase, muestra gente
 * real y reparte a las demas paginas. Cada seccion lleva a otra pagina; ninguna intenta
 * ser el sitio entero.
 */
export function Portada() {
  useTitulo('')
  const catalogo = useCatalogo()
  const comunidad = useComunidad()
  const navegar = useNavigate()

  const [rubro, setRubro] = useState('')
  const [zona, setZona] = useState('')

  const rubros = catalogo.datos?.rubros ?? []
  const zonas = catalogo.datos?.zonas ?? []
  const recientes = comunidad.datos?.recienLlegados ?? []
  const total = comunidad.datos?.total ?? 0

  function alBuscar(e: FormEvent) {
    e.preventDefault()
    const q = new URLSearchParams()
    if (rubro) q.set('rubro', rubro)
    if (zona) q.set('zona', zona)
    const texto = q.toString()
    navegar(texto ? `/buscar?${texto}` : '/buscar')
  }

  const primero = recientes[0]

  return (
    <>
      <section className="hero">
        <div className="contenedor hero-rejilla">
          <div>
            <p className="sobretitulo">Temuco · Conecta con un toque</p>
            <h1 className="titular">El que sabe vive cerca.</h1>
            <p className="bajada">
              Abogados, corredores, contadores y técnicos de tu barrio, con su cara, su zona y
              sus servicios a la vista. Buscar no te cuesta nada.
            </p>

            <form className="frase" onSubmit={alBuscar} aria-label="Buscar a alguien">
              <p className="frase-texto">
                Busco{' '}
                <span className="elige">
                  <label className="solo-lector" htmlFor="portada-rubro">Oficio</label>
                  <select id="portada-rubro" value={rubro} onChange={(e) => setRubro(e.target.value)}>
                    <option value="">un oficio</option>
                    {rubros.map((r) => <option key={r.id} value={r.codigo}>{r.nombre}</option>)}
                  </select>
                </span>{' '}
                en{' '}
                <span className="elige">
                  <label className="solo-lector" htmlFor="portada-zona">Barrio</label>
                  <select id="portada-zona" value={zona} onChange={(e) => setZona(e.target.value)}>
                    <option value="">mi barrio</option>
                    {zonas.map((z) => <option key={z.id} value={z.id}>{z.nombre}</option>)}
                  </select>
                </span>
              </p>
              <div className="frase-acciones">
                <button className="boton boton--tinta" type="submit">
                  Ver quién hay<Icono nombre="flecha" />
                </button>
                <Link className="enlace-ubicacion" to="/buscar">
                  <Icono nombre="pin" />Mejor, cerca de donde estoy
                </Link>
              </div>
              {catalogo.error && (
                <p className="aviso-linea" role="status">
                  {catalogo.error}{' '}
                  <button type="button" className="enlace" onClick={catalogo.reintentar}>
                    Reintentar
                  </button>
                </p>
              )}
            </form>
          </div>

          <div className="mazo">
            {primero ? (
              <>
                <Link to={`/prestadores/${primero.slug}`} className="tarjeta-mano">
                  <div className="tarjeta-cabeza">
                    <Monograma nombre={primero.nombrePublico} />
                    <div>
                      <p className="tarjeta-nombre">{primero.nombrePublico}</p>
                      <p className="tarjeta-detalle">{primero.rubro ?? 'Prestador'}</p>
                    </div>
                  </div>
                  <div className="fila-etiquetas">
                    {primero.verificada && (
                      <span className="sello"><Icono nombre="sello" />Verificado</span>
                    )}
                    {primero.zona && (
                      <span className="dato"><Icono nombre="pin" />{primero.zona}</span>
                    )}
                    <span className="dato">Se sumó hace poco</span>
                  </div>
                  <p className="tarjeta-pie">
                    <span>Ver su ficha</span><Icono nombre="flecha" />
                  </p>
                </Link>
                {recientes.slice(1, 3).map((r, i) => (
                  <div key={r.slug} className={`tarjeta-atras tarjeta-atras--${i + 1}`} aria-hidden="true">
                    <Monograma nombre={r.nombrePublico} tamano="p" />
                    <span>
                      {r.nombrePublico}
                      <small>{[r.rubro, r.zona].filter(Boolean).join(' · ')}</small>
                    </span>
                  </div>
                ))}
              </>
            ) : (
              <Link to="/profesionales" className="tarjeta-mano">
                <p className="tarjeta-nombre">Tu barrio todavía te espera.</p>
                <p className="tarjeta-detalle">
                  Estamos reuniendo a los primeros profesionales de Temuco. Si eres uno, tu ficha
                  puede ser la primera que vea tu vecino.
                </p>
                <p className="tarjeta-pie">
                  <span>Crear mi ficha</span><Icono nombre="flecha" />
                </p>
              </Link>
            )}
          </div>
        </div>
      </section>

      {rubros.length > 0 && (
        <div className="cinta" aria-hidden="true">
          <div className="cinta-pista">
            {[...rubros, ...rubros].map((r, i) => (
              <span key={`${r.id}-${i}`} className="cinta-item">{r.nombre}<i /></span>
            ))}
          </div>
        </div>
      )}

      {recientes.length > 0 && (
        <section className="seccion">
          <div className="contenedor">
            <p className="sobretitulo sobretitulo--tinta">Ya somos {personas(total)}</p>
            <h2 className="seccion-titulo">Se sumaron hace poco</h2>
            <ul className="tarjetas-rejilla">
              {recientes.map((r) => (
                <li key={r.slug}>
                  <TarjetaPersona p={{
                    slug: r.slug, nombre: r.nombrePublico, rubro: r.rubro,
                    zona: r.zona, verificada: r.verificada,
                  }} />
                </li>
              ))}
            </ul>
            <p className="seccion-cierre">
              <Link className="boton" to="/buscar">Buscar a alguien más<Icono nombre="flecha" /></Link>
            </p>
          </div>
        </section>
      )}

      <section className="seccion seccion--tiles">
        <div className="contenedor">
          <h2 className="seccion-titulo">Entra por donde quieras</h2>
          <div className="tiles">
            <Link to="/barrios" className="tile tile--amarillo">
              <Icono nombre="pin" />
              <h3>Por barrio</h3>
              <p>
                {zonas.length > 0 ? `${zonas.length} barrios` : 'Los barrios'} de Temuco y
                alrededores, y cuánta gente hay en cada uno.
              </p>
              <span className="tile-ir">Ver los barrios<Icono nombre="flecha" /></span>
            </Link>
            <Link to="/oficios" className="tile tile--tinta">
              <Icono nombre="llave" />
              <h3>Por oficio</h3>
              <p>
                {rubros.length > 0
                  ? `${rubros.slice(0, 3).map((r) => capitalizar(r.nombre)).join(', ')}…`
                  : 'Abogados, corredores, contadores…'}
              </p>
              <span className="tile-ir">Ver los oficios<Icono nombre="flecha" /></span>
            </Link>
            <Link to="/profesionales" className="tile tile--papel">
              <Icono nombre="persona" />
              <h3>Si eres profesional</h3>
              <p>Tu ficha, tu barrio y tus servicios, a la vista de quien te busca cerca.</p>
              <span className="tile-ir">Crear mi ficha<Icono nombre="flecha" /></span>
            </Link>
          </div>
        </div>
      </section>

      <section className="seccion oscuro sobre-tinta">
        <div className="contenedor">
          <h2 className="seccion-titulo">Hecho para que te animes a escribirle a un vecino.</h2>
          <div className="principios">
            <div className="principio">
              <Icono nombre="lupa" />
              <h3>Buscar es gratis. Siempre.</h3>
              <p>Quien busca nunca paga. Quien ofrece su trabajo paga una mensualidad por estar aquí.</p>
            </div>
            <div className="principio">
              <Icono nombre="pin" />
              <h3>Cerca, sin saber dónde vives.</h3>
              <p>Ves a qué distancia está cada persona, redondeada. Nadie ve la dirección de nadie.</p>
            </div>
            <div className="principio">
              <Icono nombre="persona" />
              <h3>Cada quien elige qué muestra.</h3>
              <p>Cada profesional decide si publica su teléfono, su WhatsApp o su correo. Tú lo ves tal cual.</p>
            </div>
          </div>
        </div>
      </section>
    </>
  )
}
