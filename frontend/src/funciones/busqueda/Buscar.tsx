import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { ErrorApi } from '../../api/cliente'
import { Aviso } from '../../componentes/Aviso'
import { Seleccion, type OpcionDeSeleccion } from '../../componentes/Seleccion'
import {
  buscar, listarRubros, listarZonas,
  type Criterios, type Resultado, type Rubro, type Zona,
} from './servicio'

interface Punto {
  lat: number
  lon: number
}

/**
 * Los radios que se ofrecen, en km. El mayor es el maximo de RN-S3-04: la API corrige
 * hacia abajo cualquier cosa mas grande, asi que ofrecer mas seria prometer lo que no hay.
 */
const RADIOS_KM = [5, 10, 20, 50] as const
const RADIO_MAXIMO_KM = RADIOS_KM[RADIOS_KM.length - 1]
const OPCIONES_DE_RADIO: OpcionDeSeleccion[] = RADIOS_KM.map((km) => ({
  valor: String(km), texto: `${km} km`,
}))

function mensajeDe(e: unknown, porDefecto: string): string {
  return e instanceof ErrorApi ? e.mensaje : porDefecto
}

function capitalizar(texto: string): string {
  return texto.charAt(0).toUpperCase() + texto.slice(1)
}

/**
 * RN-S5-11: con menos de 3 resenas el promedio no es publico y la API lo manda nulo.
 * Aqui no se inventa uno: se muestra el recuento. El promedio se escribe con coma
 * decimal a mano, y no con toLocaleString, para que no dependa de los datos de
 * idioma del navegador.
 */
function reputacion(r: Resultado): string {
  const resenas = r.totalResenas === 1 ? '1 reseña' : `${r.totalResenas} reseñas`
  if (r.promedioCalificacion !== null) {
    return `${r.promedioCalificacion.toFixed(1).replace('.', ',')} de 5 · ${resenas}`
  }
  return r.totalResenas === 0 ? 'Sin reseñas todavía' : resenas
}

/**
 * CU-08. La busqueda publica: no pide sesion (P1), porque un visitante sin cuenta
 * ejecuta el caso completo.
 */
export function Buscar() {
  const [catalogo, setCatalogo] = useState<{ rubros: Rubro[]; zonas: Zona[] } | null>(null)
  const [errorCatalogo, setErrorCatalogo] = useState<string>()

  // Lo que la persona va eligiendo en el formulario...
  const [rubro, setRubro] = useState('')
  const [zona, setZona] = useState('')
  const [punto, setPunto] = useState<Punto | null>(null)
  const [verificados, setVerificados] = useState(false)
  const [cuatroEstrellas, setCuatroEstrellas] = useState(false)
  const [errorRubro, setErrorRubro] = useState<string>()
  const [errorZona, setErrorZona] = useState<string>()
  const [avisoUbicacion, setAvisoUbicacion] = useState<string>()
  const [pidiendoUbicacion, setPidiendoUbicacion] = useState(false)
  // 10 km es lo que la API usa por omision (RN-S3-04). Solo cuenta con ubicacion.
  const [radio, setRadio] = useState('10')

  // ...y lo que la ultima busqueda devolvio. Son cosas distintas a proposito: "ver mas"
  // tiene que seguir la busqueda que genero el cursor, no lo que el formulario diga ahora.
  const [consulta, setConsulta] = useState<Criterios | null>(null)
  const [resultados, setResultados] = useState<Resultado[]>([])
  const [siguiente, setSiguiente] = useState<string | null>(null)
  const [aviso, setAviso] = useState<string | null>(null)
  const [buscado, setBuscado] = useState(false)
  // 'nueva' es una busqueda desde cero; 'mas' es "ver mas". No es lo mismo: la primera
  // cambia el boton principal y la segunda solo el de abajo.
  const [cargando, setCargando] = useState<'nueva' | 'mas' | null>(null)
  const [errorBusqueda, setErrorBusqueda] = useState<string>()

  // Numero de la ultima busqueda lanzada. Una respuesta que llega con otro numero es de
  // una busqueda que ya no es la vigente, y se descarta: sin esto, la lenta pisaria a
  // la rapida y la pantalla mostraria resultados de algo que ya no se pidio.
  const numero = useRef(0)

  // Cuantas veces se pidio cargar el catalogo. Reintentar es subir este numero, y eso
  // vuelve a disparar el efecto.
  const [intentoCatalogo, setIntentoCatalogo] = useState(0)

  useEffect(() => {
    // `vigente` descarta la respuesta de un efecto que ya fue reemplazado o desmontado.
    // Con StrictMode el efecto corre dos veces en desarrollo, y sin esto la primera
    // respuesta pisaria a la segunda.
    let vigente = true

    Promise.all([listarRubros(), listarZonas()])
      .then(([rubros, zonas]) => {
        if (vigente) setCatalogo({ rubros, zonas })
      })
      .catch((e: unknown) => {
        if (vigente) setErrorCatalogo(mensajeDe(e, 'No pudimos cargar los rubros y las zonas.'))
      })

    return () => { vigente = false }
  }, [intentoCatalogo])

  const opcionesRubro = useMemo<OpcionDeSeleccion[]>(
    () => (catalogo?.rubros ?? []).map((r) => ({ valor: r.codigo, texto: r.nombre })),
    [catalogo],
  )
  const opcionesZona = useMemo<OpcionDeSeleccion[]>(
    () => (catalogo?.zonas ?? []).map((z) => ({
      valor: String(z.id), texto: z.nombre, grupo: capitalizar(z.sector),
    })),
    [catalogo],
  )

  async function ejecutar(criterios: Criterios, cursor?: string) {
    const mio = ++numero.current
    setCargando(cursor ? 'mas' : 'nueva')
    setErrorBusqueda(undefined)

    try {
      const pagina = await buscar(cursor ? { ...criterios, cursor } : criterios)
      if (mio !== numero.current) return

      setResultados((antes) => (cursor ? [...antes, ...pagina.resultados] : pagina.resultados))
      setSiguiente(pagina.cursorSiguiente)
      setAviso(pagina.aviso)
      setBuscado(true)
      if (cursor === undefined) setConsulta(criterios)
    } catch (e) {
      if (mio !== numero.current) return

      setErrorBusqueda(mensajeDe(e, 'No pudimos buscar. Intenta de nuevo.'))
      if (cursor === undefined) {
        // Una busqueda nueva que falla no puede dejar los resultados de la anterior
        // debajo de un error: parecerian la respuesta a lo que se acaba de pedir.
        setResultados([])
        setSiguiente(null)
        setBuscado(false)
      }
    } finally {
      if (mio === numero.current) setCargando(null)
    }
  }

  function alEnviar(e: FormEvent) {
    e.preventDefault()

    const faltaRubro = rubro === '' ? 'Elige qué necesitas.' : undefined
    const faltaDonde = zona === '' && punto === null
      ? 'Elige una zona o usa tu ubicación.'
      : undefined
    setErrorRubro(faltaRubro)
    setErrorZona(faltaDonde)
    if (faltaRubro || faltaDonde) return

    // Punto o zona, nunca los dos: con punto se ordena por distancia y con zona no
    // (RN-S3-07), y mandar ambos dejaria a la API decidir cual manda.
    void ejecutar({
      rubro,
      ...(punto
        ? { lat: punto.lat, lon: punto.lon, radio: Number(radio) }
        : { zona: Number(zona) }),
      ...(verificados ? { verificados: true } : {}),
      ...(cuatroEstrellas ? { calificacion: 4 } : {}),
    })
  }

  function usarUbicacion() {
    if (!navigator.geolocation) {
      setAvisoUbicacion(
        'Tu navegador no permite usar la ubicación. Elige tu zona de la lista.')
      return
    }

    setPidiendoUbicacion(true)
    setAvisoUbicacion(undefined)
    navigator.geolocation.getCurrentPosition(
      (posicion) => {
        // Las coordenadas viven en memoria y viajan a la API solo al buscar. No se
        // guardan en ningun lado: es la posicion de una persona.
        setPunto({ lat: posicion.coords.latitude, lon: posicion.coords.longitude })
        setZona('')
        setErrorZona(undefined)
        setPidiendoUbicacion(false)
      },
      () => {
        // Flujo 4A de CU-08: si la persona la niega, no se insiste ni se vuelve a
        // pedir. Esto solo se dispara al pulsar el boton, nunca solo.
        setAvisoUbicacion('No pudimos usar tu ubicación. Elige tu zona de la lista.')
        setPidiendoUbicacion(false)
      },
      { timeout: 10_000, maximumAge: 60_000 },
    )
  }

  function alCambiarZona(valor: string) {
    setZona(valor)
    setErrorZona(undefined)
    // Elegir una zona a mano es decir "busca aqui", y eso descarta el punto.
    if (valor !== '') setPunto(null)
  }

  const hayFiltros = consulta !== null
    && (consulta.verificados === true || consulta.calificacion !== undefined)

  // Flujo 4B de CU-08: sin resultados, lo primero que se propone es ampliar el radio. Se
  // mira la busqueda que SE HIZO (`consulta`), no el formulario: la persona puede haber
  // tocado el control despues sin volver a buscar.
  const puedeAmpliar = consulta !== null && consulta.lat !== undefined
    && consulta.radio !== undefined && consulta.radio < RADIO_MAXIMO_KM

  const sugerencia = puedeAmpliar
    ? 'Prueba ampliando el radio.'
    : hayFiltros ? 'Prueba quitando los filtros.' : 'Prueba con otra zona.'

  function ampliar() {
    if (consulta === null) return
    setRadio(String(RADIO_MAXIMO_KM))   // el formulario refleja lo que se busca ahora
    void ejecutar({ ...consulta, radio: RADIO_MAXIMO_KM })
  }

  return (
    <section className="buscador">
      <form className="tarjeta tarjeta--ancha" onSubmit={alEnviar} noValidate>
        <h1 className="titulo">Buscar</h1>
        <p className="bajada">Encuentra a quien atiende cerca de ti</p>

        {errorCatalogo && (
          <>
            <Aviso>{errorCatalogo}</Aviso>
            <button className="boton boton--claro" type="button" onClick={() => { setErrorCatalogo(undefined); setIntentoCatalogo((n) => n + 1) }}>
              Reintentar
            </button>
          </>
        )}

        <Seleccion
          id="rubro" etiqueta="¿Qué necesitas?" vacio="Elige un rubro"
          valor={rubro} alCambiar={(v) => { setRubro(v); setErrorRubro(undefined) }}
          opciones={opcionesRubro} error={errorRubro}
        />

        <Seleccion
          id="zona" etiqueta="¿En qué zona?" vacio="Elige una zona"
          valor={zona} alCambiar={alCambiarZona}
          opciones={opcionesZona} error={errorZona}
        />

        <div className="ubicacion">
          {punto ? (
            <>
              <p className="ubicacion-estado">
                Usando tu ubicación.{' '}
                <button className="enlace" type="button" onClick={() => setPunto(null)}>
                  Quitar
                </button>
              </p>
              {/* Solo con ubicacion: con zona no hay desde donde medir, y un control que no
                  hace nada se mueve esperando que cambie algo. */}
              <Seleccion
                id="radio" etiqueta="¿Hasta qué distancia?"
                valor={radio} alCambiar={setRadio} opciones={OPCIONES_DE_RADIO}
              />
            </>
          ) : (
            <button
              className="boton boton--claro" type="button"
              onClick={usarUbicacion} disabled={pidiendoUbicacion}
            >
              {pidiendoUbicacion ? 'Buscando tu ubicación...' : 'Usar mi ubicación'}
            </button>
          )}
          {avisoUbicacion && <p className="campo-ayuda">{avisoUbicacion}</p>}
        </div>

        <fieldset className="filtros">
          <legend className="campo-etiqueta">Filtrar</legend>
          <label className="casilla">
            <input
              type="checkbox" checked={verificados}
              onChange={(e) => setVerificados(e.target.checked)}
            />
            <span>Solo verificados</span>
          </label>
          <label className="casilla">
            <input
              type="checkbox" checked={cuatroEstrellas}
              onChange={(e) => setCuatroEstrellas(e.target.checked)}
            />
            <span>4 estrellas o más</span>
          </label>
          <p className="campo-ayuda">
            El filtro de estrellas solo cuenta a quienes ya tienen 3 reseñas.
          </p>
        </fieldset>

        <button className="boton" type="submit">
          {cargando === 'nueva' ? 'Buscando...' : 'Buscar'}
        </button>
      </form>

      <div className="resultados-zona" aria-live="polite">
        {errorBusqueda && <Aviso>{errorBusqueda}</Aviso>}

        {aviso && <p className="aviso aviso--info" role="status">{aviso}</p>}

        {buscado && resultados.length === 0 && !errorBusqueda && (
          <div className="vacio">
            <p className="vacio-titulo">No encontramos a nadie con esos datos.</p>
            <p className="bajada">{sugerencia}</p>
            {puedeAmpliar && (
              <button className="boton boton--claro" type="button" onClick={ampliar}>
                Ampliar a {RADIO_MAXIMO_KM} km
              </button>
            )}
          </div>
        )}

        {resultados.length > 0 && (
          <>
            <p className="resultados-cuenta">
              {resultados.length === 1 ? '1 resultado' : `${resultados.length} resultados`}
              {siguiente !== null && ' · hay más'}
            </p>
            <ul className="resultados">
              {resultados.map((r) => (
                <li className="resultado" key={r.perfilId}>
                  <div className="resultado-cabeza">
                    <h2 className="resultado-nombre">{r.nombrePublico}</h2>
                    {r.esTuPerfil && <span className="etiqueta">Este eres tú</span>}
                  </div>

                  {(r.zonaPrincipal || r.distancia) && (
                    <p className="resultado-meta">
                      {[r.zonaPrincipal, r.distancia].filter(Boolean).join(' · ')}
                    </p>
                  )}

                  {r.tieneCredencialVerificada && (
                    <p className="sello">
                      <svg
                        className="sello-icono" viewBox="0 0 16 16" width="14" height="14"
                        aria-hidden="true" focusable="false"
                      >
                        <path
                          d="M3 8.5l3.2 3L13 4.5" fill="none" stroke="currentColor"
                          strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"
                        />
                      </svg>
                      Verificado
                    </p>
                  )}

                  <p className="resultado-reputacion">{reputacion(r)}</p>

                  {r.descripcion && <p className="resultado-descripcion">{r.descripcion}</p>}
                </li>
              ))}
            </ul>

            {siguiente !== null && consulta !== null && (
              <button
                className="boton boton--claro" type="button" disabled={cargando !== null}
                onClick={() => void ejecutar(consulta, siguiente)}
              >
                {cargando === 'mas' ? 'Cargando...' : 'Ver más'}
              </button>
            )}
          </>
        )}
      </div>

      <p className="pie">
        Zonas con datos de{' '}
        <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noreferrer noopener">
          OpenStreetMap contributors
        </a>.
      </p>
    </section>
  )
}
