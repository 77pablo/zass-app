import { useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import { useCatalogo } from '../api/useCargado'
import { buscar, type Criterios, type Resultado } from '../funciones/busqueda/servicio'
import { Icono } from '../marco/Icono'
import { TarjetaPersona } from '../marco/TarjetaPersona'
import { capitalizar, reputacion } from '../marco/texto'
import { useTitulo } from '../marco/useTitulo'

interface Punto {
  lat: number
  lon: number
}

/**
 * Los radios que se ofrecen, en km. El mayor es el maximo de RN-S3-04: la API corrige hacia
 * abajo cualquier cosa mas grande, asi que ofrecer mas seria prometer lo que no hay.
 */
const RADIOS_KM = [5, 10, 20, 50] as const
const RADIO_MAXIMO_KM = RADIOS_KM[RADIOS_KM.length - 1]

/**
 * La posicion de la persona vive SOLO en memoria, nunca en la URL. Una URL se comparte, se
 * guarda en el historial y se manda por WhatsApp: ahi la ubicacion de alguien iria a parar a
 * quien no la pidio. En memoria sobrevive a ir a una ficha y volver, y se pierde al recargar.
 */
let puntoEnMemoria: Punto | null = null

interface Pagina {
  clave: string
  resultados: Resultado[]
  siguiente: string | null
  aviso: string | null
  error: string | null
}

/**
 * CU-08 como pagina. Lo que se busca esta en la URL (oficio, barrio, filtros): se puede
 * compartir, volver atras y recargar sin perderlo.
 */
export function PaginaBuscar() {
  const [params, setParams] = useSearchParams()
  const catalogo = useCatalogo()

  const rubro = params.get('rubro') ?? ''
  const zona = params.get('zona') ?? ''
  const cerca = params.get('cerca') === '1'
  const radio = Number(params.get('radio')) || 10
  const soloVerificados = params.get('verificados') === '1'
  const cuatroEstrellas = params.get('cal') === '4'

  const [punto, setPunto] = useState<Punto | null>(() => (cerca ? puntoEnMemoria : null))
  const [avisoUbicacion, setAvisoUbicacion] = useState<string>()
  const [pidiendo, setPidiendo] = useState(false)

  const rubros = catalogo.datos?.rubros
  const zonas = catalogo.datos?.zonas
  const rubroElegido = rubros?.find((r) => r.codigo === rubro)
  const zonaElegida = zonas?.find((z) => String(z.id) === zona)

  useTitulo(
    rubroElegido
      ? `${capitalizar(rubroElegido.nombre)}${zonaElegida ? ` en ${zonaElegida.nombre}` : ''}`
      : 'Buscar',
  )

  // Lo que se le pide a la API, o null si todavia falta algo. Punto o zona, nunca los dos:
  // con punto se ordena por distancia y con zona no (RN-S3-07).
  const criterios = useMemo<Criterios | null>(() => {
    if (rubro === '') return null
    const filtros = {
      ...(soloVerificados ? { verificados: true } : {}),
      ...(cuatroEstrellas ? { calificacion: 4 } : {}),
    }
    if (cerca && punto) return { rubro, lat: punto.lat, lon: punto.lon, radio, ...filtros }
    if (!cerca && zona !== '') return { rubro, zona: Number(zona), ...filtros }
    return null
  }, [rubro, zona, cerca, punto, radio, soloVerificados, cuatroEstrellas])

  const clave = criterios === null ? '' : JSON.stringify(criterios)

  const [pagina, setPagina] = useState<Pagina | null>(null)
  const [cargandoMas, setCargandoMas] = useState(false)

  useEffect(() => {
    if (criterios === null) return
    // `vigente` descarta la respuesta de una busqueda que ya no es la que se pidio: sin esto,
    // la lenta pisaria a la rapida y la pantalla mostraria resultados de otra cosa.
    let vigente = true
    buscar(criterios)
      .then((p) => {
        if (vigente) setPagina({
          clave, resultados: p.resultados, siguiente: p.cursorSiguiente, aviso: p.aviso, error: null,
        })
      })
      .catch((e: unknown) => {
        if (vigente) setPagina({
          clave, resultados: [], siguiente: null, aviso: null,
          error: e instanceof ErrorApi ? e.mensaje : 'No pudimos buscar. Intenta de nuevo.',
        })
      })
    return () => { vigente = false }
    // `criterios` esta resumido en `clave`: depender de ambos repetiria la busqueda
    // cada vez que useMemo devuelva un objeto nuevo igual al anterior.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [clave])

  const actual = pagina !== null && pagina.clave === clave ? pagina : null
  const cargando = criterios !== null && actual === null

  function cambiar(cambios: Record<string, string | null>) {
    const siguiente = new URLSearchParams(params)
    for (const [k, v] of Object.entries(cambios)) {
      if (v === null || v === '') siguiente.delete(k)
      else siguiente.set(k, v)
    }
    setParams(siguiente, { replace: true })
  }

  function usarUbicacion() {
    if (!navigator.geolocation) {
      setAvisoUbicacion('Tu navegador no permite usar la ubicación. Elige tu barrio de la lista.')
      return
    }
    setPidiendo(true)
    setAvisoUbicacion(undefined)
    navigator.geolocation.getCurrentPosition(
      (posicion) => {
        const p = { lat: posicion.coords.latitude, lon: posicion.coords.longitude }
        puntoEnMemoria = p
        setPunto(p)
        setPidiendo(false)
        cambiar({ cerca: '1', zona: null })
      },
      () => {
        // Flujo 4A de CU-08: si la persona la niega no se insiste ni se vuelve a pedir. Esto
        // solo se dispara al pulsar el boton, nunca solo.
        setAvisoUbicacion('No pudimos usar tu ubicación. Elige tu barrio de la lista.')
        setPidiendo(false)
      },
      { timeout: 10_000, maximumAge: 60_000 },
    )
  }

  function quitarUbicacion() {
    puntoEnMemoria = null
    setPunto(null)
    cambiar({ cerca: null, radio: null })
  }

  async function verMas() {
    if (actual === null || actual.siguiente === null || criterios === null) return
    setCargandoMas(true)
    try {
      const p = await buscar({ ...criterios, cursor: actual.siguiente })
      setPagina({
        ...actual,
        resultados: [...actual.resultados, ...p.resultados],
        siguiente: p.cursorSiguiente,
        aviso: p.aviso ?? actual.aviso,
      })
    } catch (e) {
      setPagina({
        ...actual,
        error: e instanceof ErrorApi ? e.mensaje : 'No pudimos cargar más. Intenta de nuevo.',
      })
    } finally {
      setCargandoMas(false)
    }
  }

  const hayFiltros = soloVerificados || cuatroEstrellas
  // Flujo 4B de CU-08: sin resultados, lo primero que se propone es ampliar el radio.
  const puedeAmpliar = cerca && punto !== null && radio < RADIO_MAXIMO_KM
  const sugerencia = puedeAmpliar
    ? 'Prueba ampliando el radio.'
    : hayFiltros ? 'Prueba quitando los filtros.' : 'Prueba con otro barrio u otro oficio.'

  const titulo = rubroElegido
    ? `${capitalizar(rubroElegido.nombre)}${
      zonaElegida ? ` en ${zonaElegida.nombre}` : cerca && punto ? ' cerca de ti' : ''}`
    : 'Busca a alguien de tu barrio'

  // Sin oficio todavia: la lista entera de oficios, para empezar con un toque.
  const faltaOficio = rubro === ''
  const faltaDonde = !faltaOficio && criterios === null

  return (
    <div className="pagina">
      <section className="pagina-cabeza pagina-cabeza--amarillo">
        <div className="contenedor">
          <p className="sobretitulo">Buscar</p>
          <h1 className="pagina-titulo">{titulo}</h1>
        </div>
      </section>

      <div className="contenedor buscar-cuerpo">
        <form className="filtros-barra" onSubmit={(e) => e.preventDefault()} aria-label="Filtros de búsqueda">
          <div className="filtro">
            <label htmlFor="b-rubro">Oficio</label>
            <select id="b-rubro" value={rubro} onChange={(e) => cambiar({ rubro: e.target.value })}>
              <option value="">Elige un oficio</option>
              {(rubros ?? []).map((r) => <option key={r.id} value={r.codigo}>{r.nombre}</option>)}
            </select>
          </div>

          <div className="filtro">
            <label htmlFor="b-zona">Barrio</label>
            <select
              id="b-zona" value={cerca ? '' : zona}
              onChange={(e) => {
                puntoEnMemoria = null
                setPunto(null)
                cambiar({ zona: e.target.value, cerca: null, radio: null })
              }}
            >
              <option value="">{cerca ? 'Cerca de ti' : 'Elige un barrio'}</option>
              {(zonas ?? []).map((z) => (
                <option key={z.id} value={z.id}>{z.nombre} · {capitalizar(z.sector)}</option>
              ))}
            </select>
          </div>

          {cerca && punto ? (
            <div className="filtro">
              <label htmlFor="b-radio">Hasta qué distancia</label>
              <select
                id="b-radio" value={String(radio)}
                onChange={(e) => cambiar({ radio: e.target.value })}
              >
                {RADIOS_KM.map((km) => <option key={km} value={km}>{km} km</option>)}
              </select>
            </div>
          ) : null}

          <div className="filtro filtro--accion">
            {cerca && punto ? (
              <button type="button" className="boton boton--chico" onClick={quitarUbicacion}>
                <Icono nombre="cerrar" />Quitar mi ubicación
              </button>
            ) : (
              <button
                type="button" className="boton boton--chico" onClick={usarUbicacion} disabled={pidiendo}
              >
                <Icono nombre="pin" />{pidiendo ? 'Buscando tu ubicación…' : 'Usar mi ubicación'}
              </button>
            )}
          </div>

          <fieldset className="filtro-casillas">
            <legend className="solo-lector">Filtrar</legend>
            <label className="casilla">
              <input
                type="checkbox" checked={soloVerificados}
                onChange={(e) => cambiar({ verificados: e.target.checked ? '1' : null })}
              />
              <span>Solo verificados</span>
            </label>
            <label className="casilla">
              <input
                type="checkbox" checked={cuatroEstrellas}
                onChange={(e) => cambiar({ cal: e.target.checked ? '4' : null })}
              />
              <span>4 estrellas o más</span>
            </label>
          </fieldset>
        </form>

        {avisoUbicacion && <p className="aviso-linea" role="status">{avisoUbicacion}</p>}
        {cerca && punto === null && !avisoUbicacion && (
          <p className="aviso-linea" role="status">
            Para buscar cerca de ti, pulsa «Usar mi ubicación». No guardamos dónde estás.
          </p>
        )}
        {catalogo.error && (
          <p className="aviso-linea" role="alert">
            {catalogo.error}{' '}
            <button type="button" className="enlace" onClick={catalogo.reintentar}>Reintentar</button>
          </p>
        )}

        <div aria-live="polite">
          {faltaOficio && rubros && (
            <section className="elige-oficio">
              <h2 className="bloque-titulo">¿Qué necesitas?</h2>
              <ul className="chips">
                {rubros.map((r) => (
                  <li key={r.id}>
                    <button type="button" className="chip" onClick={() => cambiar({ rubro: r.codigo })}>
                      {r.nombre}
                    </button>
                  </li>
                ))}
              </ul>
            </section>
          )}

          {faltaDonde && (
            <p className="vacio-linea">
              Ahora elige un barrio, o usa tu ubicación para ver quién atiende cerca.
            </p>
          )}

          {cargando && <p className="cargando-linea" role="status">Buscando…</p>}

          {actual?.error && <p className="aviso-linea" role="alert">{actual.error}</p>}
          {actual?.aviso && <p className="aviso-linea" role="status">{actual.aviso}</p>}

          {actual && !actual.error && actual.resultados.length === 0 && (
            <div className="vacio">
              <h2 className="vacio-titulo">Todavía no encontramos a nadie con esos datos.</h2>
              <p>{sugerencia}</p>
              <p className="vacio-acciones">
                {puedeAmpliar && (
                  <button
                    type="button" className="boton"
                    onClick={() => cambiar({ radio: String(RADIO_MAXIMO_KM) })}
                  >
                    Ampliar a {RADIO_MAXIMO_KM} km
                  </button>
                )}
                <Link to="/profesionales" className="enlace">¿Eres de este oficio? Súmate</Link>
              </p>
            </div>
          )}

          {actual && actual.resultados.length > 0 && (
            <>
              <p className="resultados-cuenta">
                {actual.resultados.length === 1 ? '1 persona' : `${actual.resultados.length} personas`}
                {actual.siguiente !== null && ' · hay más'}
              </p>
              <ul className="tarjetas-rejilla">
                {actual.resultados.map((r) => (
                  <li key={r.perfilId}>
                    <TarjetaPersona p={{
                      slug: r.slug,
                      nombre: r.nombrePublico,
                      rubro: rubroElegido?.nombre ?? null,
                      zona: r.zonaPrincipal,
                      verificada: r.tieneCredencialVerificada,
                      detalle: [r.distancia, reputacion(r.promedioCalificacion, r.totalResenas)]
                        .filter(Boolean).join(' · '),
                      descripcion: r.descripcion,
                      esTuPerfil: r.esTuPerfil,
                    }} />
                  </li>
                ))}
              </ul>
              {actual.siguiente !== null && (
                <p className="seccion-cierre">
                  <button type="button" className="boton" disabled={cargandoMas} onClick={() => void verMas()}>
                    {cargandoMas ? 'Cargando…' : 'Ver más'}
                  </button>
                </p>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  )
}
