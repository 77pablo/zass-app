import { useCallback, useRef, type PointerEvent } from 'react'
import { Link } from 'react-router-dom'
import { personasEnZona } from '../api/comunidad'
import { useCatalogo, useComunidad, type Cargado } from '../api/useCargado'
import { Aparece } from '../marco/Aparece'
import { BarraDeBusqueda } from '../marco/BarraDeBusqueda'
import { Icono } from '../marco/Icono'
import { Monograma } from '../marco/Monograma'
import { MapaDeChile } from '../marco/MapaDeChile'
import { TarjetaPersona } from '../marco/TarjetaPersona'
import { capitalizar, EJEMPLOS, personas } from '../marco/texto'
import { useContador } from '../marco/useContador'
import { useTitulo } from '../marco/useTitulo'
import { useVisto } from '../marco/useVisto'

/** Un numero grande con su etiqueta. Sube cuando entra en pantalla; el valor final va en el texto accesible. */
function Cifra({ valor, etiqueta }: { valor: number; etiqueta: string }) {
  const ref = useRef<HTMLDivElement>(null)
  const visto = useVisto(ref)
  const visible = useContador(visto ? valor : 0, 1400)
  return (
    <div className="cifra" ref={ref}>
      <span className="cifra-numero" aria-hidden="true">{visible}</span>
      <span className="solo-lector">{valor}</span>
      <span className="cifra-etiqueta">{etiqueta}</span>
    </div>
  )
}

/**
 * Lo que la portada tiene que decir mientras no hay datos.
 *
 * Casi todas sus secciones viven tras un `&&` (el mapa, la cinta, las cifras, los recien
 * llegados): sin datos no se ven, y sin esto desaparecian SIN UNA PALABRA. La API duerme
 * (Render, plan gratis, ~15 min), asi que la primera visita del dia veia media portada muda.
 * Mismo patron que /oficios y /comunas: que esta cargando, que fallo, y como reintentar.
 */
function EstadoDeLaPortada({ cargas }: { cargas: Cargado<unknown>[] }) {
  const fallidas = cargas.filter((c) => c.error !== null)

  if (fallidas.length > 0) {
    return (
      <p className="aviso-linea" role="alert">
        {[...new Set(fallidas.map((c) => c.error))].join(' ')}{' '}
        <button
          type="button" className="enlace"
          onClick={() => { for (const c of fallidas) c.reintentar() }}
        >
          Reintentar
        </button>
      </p>
    )
  }

  if (cargas.some((c) => c.cargando)) {
    return (
      <p className="cargando-linea" role="status">
        <span className="pulso" aria-hidden="true" />
        Cargando el mapa y la comunidad… la primera vez puede tardar unos segundos.
      </p>
    )
  }

  return null
}

const OFICIOS_EN_CINTA =['abogado', 'contador', 'gasfiter', 'arquitecto', 'electricista', 'corredor', 'pintor', 'tasador', 'ingeniero', 'técnico']

/**
 * La portada es corta a proposito: dice que es ZASS!, deja buscar en una barra, muestra la ciudad y la gente real
 * y reparte a las demas paginas. Cada seccion lleva a otra pagina; ninguna intenta ser el sitio entero.
 */
export function Portada() {
  useTitulo('')
  const catalogo = useCatalogo()
  const comunidad = useComunidad()

  const rubros = catalogo.datos?.rubros ?? []
  const zonas = catalogo.datos?.zonas ?? []
  const comunas = zonas.filter((z) => z.tipo === 'Comuna')
  const recientes = comunidad.datos?.recienLlegados ?? []
  const total = comunidad.datos?.total ?? 0

  const datosComunidad = comunidad.datos
  const conteo = useCallback((id: number) => personasEnZona(datosComunidad, id), [datosComunidad])

  // El foco de luz de la portada sigue al puntero. Es adorno: sin puntero se queda donde esta.
  function seguir(e: PointerEvent<HTMLElement>) {
    // Solo con raton: con el dedo el evento se dispara al hacer scroll y repinta el fondo para nada.
    if (e.pointerType !== 'mouse') return
    const r = e.currentTarget.getBoundingClientRect()
    e.currentTarget.style.setProperty('--mx', `${e.clientX - r.left}px`)
    e.currentTarget.style.setProperty('--my', `${e.clientY - r.top}px`)
  }

  return (
    <>
      <section className="hero" onPointerMove={seguir}>
        <div className="hero-fondo" aria-hidden="true"><span className="hero-foco" /><span className="orbe orbe--1" /><span className="orbe orbe--2" /></div>
        <div className="contenedor hero-rejilla">
          <div className="hero-texto">
            <p className="sobretitulo sobretitulo--vivo entra" style={{ '--d': '0ms' } as React.CSSProperties}>
              <span className="pulso" aria-hidden="true" />
              Chile{total > 0 && <> · {personas(total)} conectadas</>}
            </p>
            <h1 className="titular entra" style={{ '--d': '90ms' } as React.CSSProperties}>
              Busca a alguien <span className="titular-brillo">de tu ciudad.</span>
            </h1>
            <p className="bajada entra" style={{ '--d': '200ms' } as React.CSSProperties}>
              Escribe un oficio, un servicio o un nombre. Te mostramos quién está más cerca, quién es más
              recomendado y cuánto cobra.
            </p>

            <div className="entra entra--arriba" style={{ '--d': '320ms' } as React.CSSProperties}>
              <BarraDeBusqueda variante="portada" />
            </div>

            <ul className="atajos entra" style={{ '--d': '440ms' } as React.CSSProperties} aria-label="Búsquedas frecuentes">
              {EJEMPLOS.map((e) => (
                <li key={e}>
                  <Link className="chip" to={`/buscar?q=${encodeURIComponent(e)}`}>{e}</Link>
                </li>
              ))}
            </ul>

            <EstadoDeLaPortada cargas={[catalogo, comunidad]} />

            {recientes.length > 0 && (
              <div className="flotantes entra" style={{ '--d': '560ms' } as React.CSSProperties} aria-label="Gente que se sumó hace poco">
                {recientes.slice(0, 3).map((r, i) => (
                  <Link key={r.slug} to={`/prestadores/${r.slug}`} className={`flotante flotante--${i + 1}`}>
                    <Monograma nombre={r.nombrePublico} tamano="p" />
                    <span>
                      <strong>{r.nombrePublico}</strong>
                      <small>{[r.rubro, r.zona].filter(Boolean).join(' · ')}</small>
                    </span>
                    {r.verificada && <Icono nombre="sello" />}
                  </Link>
                ))}
              </div>
            )}
          </div>

          {zonas.length > 0 && (
            <div className="hero-mapa entra entra--derecha" style={{ '--d': '300ms' } as React.CSSProperties}>
              <MapaDeChile zonas={zonas} conteo={conteo} />
            </div>
          )}
        </div>
      </section>

      {rubros.length > 0 && (
        <div className="cinta" aria-hidden="true">
          <div className="cinta-pista">
            {[...rubros, ...rubros].map((r, i) => (
              <span key={`${r.id}-${i}`} className="cinta-item">{r.nombre}<i /></span>
            ))}
          </div>
          <div className="cinta-pista cinta-pista--inversa">
            {[...OFICIOS_EN_CINTA, ...OFICIOS_EN_CINTA, ...OFICIOS_EN_CINTA].map((o, i) => (
              <span key={`${o}-${i}`} className="cinta-chip">{o}</span>
            ))}
          </div>
        </div>
      )}

      {total > 0 && (
        <section className="cifras" aria-label="ZASS! en cifras">
          <div className="contenedor cifras-fila">
            <Cifra valor={total} etiqueta="personas con ficha" />
            <Cifra valor={comunas.length} etiqueta="comunas" />
            <Cifra valor={rubros.length} etiqueta="oficios" />
          </div>
        </section>
      )}

      <section className="seccion">
        <div className="contenedor">
          <Aparece>
            <p className="sobretitulo sobretitulo--tinta">Cómo funciona</p>
            <h2 className="seccion-titulo">Tres pasos, ninguna llamada de más.</h2>
          </Aparece>
          <ol className="pasos">
            <li>
              <Aparece orden={0}>
                <span className="paso-numero" aria-hidden="true">1</span>
                <h3>Escribe lo que necesitas</h3>
                <p>«Abogado», «gasfiter», «divorcio» o el nombre de alguien. No hace falta saber cómo se llama el rubro.</p>
              </Aparece>
            </li>
            <li>
              <Aparece orden={1}>
                <span className="paso-numero" aria-hidden="true">2</span>
                <h3>Compara a tu manera</h3>
                <p>Ordena por quién está más cerca, quién es más recomendado, o por precio: del más barato o del más caro.</p>
              </Aparece>
            </li>
            <li>
              <Aparece orden={2}>
                <span className="paso-numero" aria-hidden="true">3</span>
                <h3>Habla directo</h3>
                <p>Cada ficha trae el contacto que la persona eligió publicar. Sin intermediarios.</p>
              </Aparece>
            </li>
          </ol>
        </div>
      </section>

      {recientes.length > 0 && (
        <section className="seccion seccion--panel">
          <div className="contenedor">
            <Aparece>
              <p className="sobretitulo sobretitulo--tinta">Recién llegados</p>
              <h2 className="seccion-titulo">Se sumaron hace poco</h2>
            </Aparece>
            <ul className="tarjetas-rejilla">
              {recientes.map((r, i) => (
                <li key={r.slug}>
                  <Aparece orden={i % 3}>
                    <TarjetaPersona p={{
                      slug: r.slug, nombre: r.nombrePublico, rubro: r.rubro,
                      zona: r.zona, verificada: r.verificada,
                    }} />
                  </Aparece>
                </li>
              ))}
            </ul>
            <p className="seccion-cierre">
              <Link className="boton" to="/buscar">Ver a toda la comunidad<Icono nombre="flecha" /></Link>
            </p>
          </div>
        </section>
      )}

      <section className="seccion seccion--tiles">
        <div className="contenedor">
          <Aparece><h2 className="seccion-titulo">Entra por donde quieras</h2></Aparece>
          <div className="tiles">
            <Aparece orden={0}>
              <Link to="/comunas" className="tile tile--amarillo">
                <Icono nombre="pin" />
                <h3>Por comuna</h3>
                <p>
                  {comunas.length > 0 ? `${comunas.length} comunas` : 'Las comunas'} de todo Chile,
                  y cuánta gente hay en cada una.
                </p>
                <span className="tile-ir">Ver las comunas<Icono nombre="flecha" /></span>
              </Link>
            </Aparece>
            <Aparece orden={1}>
              <Link to="/oficios" className="tile tile--vidrio">
                <Icono nombre="llave" />
                <h3>Por oficio</h3>
                <p>
                  {rubros.length > 0
                    ? `${rubros.slice(0, 3).map((r) => capitalizar(r.nombre)).join(', ')}…`
                    : 'Abogados, corredores, contadores…'}
                </p>
                <span className="tile-ir">Ver los oficios<Icono nombre="flecha" /></span>
              </Link>
            </Aparece>
            <Aparece orden={2}>
              <Link to="/profesionales" className="tile tile--vidrio">
                <Icono nombre="persona" />
                <h3>Si eres profesional</h3>
                <p>Tu ficha, tu comuna y tus servicios, a la vista de quien te busca cerca.</p>
                <span className="tile-ir">Crear mi ficha<Icono nombre="flecha" /></span>
              </Link>
            </Aparece>
          </div>
        </div>
      </section>

      <section className="seccion seccion--panel">
        <div className="contenedor">
          <Aparece><h2 className="seccion-titulo">Hecho para que te animes a escribirle a un vecino.</h2></Aparece>
          <div className="principios">
            <Aparece orden={0} className="principio">
              <Icono nombre="lupa" />
              <h3>Buscar es gratis. Siempre.</h3>
              <p>Quien busca nunca paga. Quien ofrece su trabajo paga una mensualidad por estar aquí.</p>
            </Aparece>
            <Aparece orden={1} className="principio">
              <Icono nombre="pin" />
              <h3>Cerca, sin saber dónde vives.</h3>
              <p>Ves a qué distancia está cada persona, redondeada. Nadie ve la dirección de nadie.</p>
            </Aparece>
            <Aparece orden={2} className="principio">
              <Icono nombre="persona" />
              <h3>Cada quien elige qué muestra.</h3>
              <p>Cada profesional decide si publica su teléfono, su WhatsApp o su correo. Tú lo ves tal cual.</p>
            </Aparece>
          </div>
        </div>
      </section>
    </>
  )
}
