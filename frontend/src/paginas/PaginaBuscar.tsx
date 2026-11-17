import { useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import { useCatalogo } from '../api/useCargado'
import {
  buscarTexto, type CriteriosDeTexto, type OrdenDeTexto, type PaginaDeTexto, type ResultadoDeTexto,
} from '../funciones/busqueda/servicio'
import { BarraDeBusqueda } from '../marco/BarraDeBusqueda'
import { Icono } from '../marco/Icono'
import { TarjetaPersona } from '../marco/TarjetaPersona'
import { capitalizar, personas, reputacion } from '../marco/texto'
import { guardarUbicacion, olvidarUbicacion, ubicacionGuardada } from '../marco/ubicacion'
import { useTitulo } from '../marco/useTitulo'
import { agruparPorRegion, etiquetaDeZona } from '../marco/zonas'

const ORDENES: { valor: OrdenDeTexto; texto: string; icono: 'estrella' | 'pin' | 'mas' }[] = [
  { valor: 'Recomendados', texto: 'Más recomendados', icono: 'estrella' },
  { valor: 'Cercania', texto: 'Más cerca', icono: 'pin' },
  { valor: 'PrecioAsc', texto: 'Menor precio', icono: 'mas' },
  { valor: 'PrecioDesc', texto: 'Mayor precio', icono: 'mas' },
]

function ordenDe(valor: string | null): OrdenDeTexto {
  return ORDENES.some((o) => o.valor === valor) ? (valor as OrdenDeTexto) : 'Recomendados'
}

/**
 * Hasta donde buscar (RN-S3-04). El maximo del documento es 50 km; el minimo, 1. Estos cuatro son
 * los que se ofrecen: pasos que una persona entiende sin pensar, no un deslizador de kilometros.
 *
 * Este control existio, se perdio al pasar el sitio a varias paginas y nadie lo noto durante
 * semanas: sus pruebas eran de la capa de servicio, que seguia sabiendo filtrar por radio mientras
 * ninguna pantalla lo pedia. Por eso ahora hay una prueba que mira la PANTALLA.
 */
const RADIOS = [5, 10, 20, 50] as const
const RADIO_MAXIMO = 50

function radioDe(valor: string | null): number | null {
  const n = Number(valor)
  return valor !== null && RADIOS.some((r) => r === n) ? n : null
}

interface Acumulado {
  clave: string
  resultados: ResultadoDeTexto[]
  total: number
  pagina: number
  hayMas: boolean
  ordenAplicado: OrdenDeTexto
  aviso: string | null
  error: string | null
}

function desdePagina(clave: string, p: PaginaDeTexto, antes: ResultadoDeTexto[] = []): Acumulado {
  return {
    clave, resultados: [...antes, ...p.resultados], total: p.total, pagina: p.pagina, hayMas: p.hayMas,
    ordenAplicado: p.ordenAplicado, aviso: p.aviso, error: null,
  }
}

/**
 * La pagina de busqueda: una barra arriba, el orden como botones y la lista debajo. Todo lo que se
 * pide esta en la URL (texto, orden, comuna), asi que se comparte, se recarga y el boton Atras funciona.
 * Sin texto muestra a toda la comunidad.
 */
export function PaginaBuscar() {
  const [params, setParams] = useSearchParams()
  const catalogo = useCatalogo()

  const q = params.get('q') ?? ''
  const zona = params.get('zona') ?? ''
  const cerca = params.get('cerca') === '1'
  const ordenPedido = ordenDe(params.get('orden'))
  const soloVerificados = params.get('verificados') === '1'
  const cuatroEstrellas = params.get('cal') === '4'
  const radio = radioDe(params.get('radio'))

  // La URL manda: «cerca=1» dice que se busca desde la persona, y el punto sale de la memoria. No hay un
  // estado aparte que pueda cambiar antes que la URL (React Router navega dentro de una transicion): eso
  // hacia, por un instante, una busqueda «mas cerca» sin ubicacion.
  const punto = cerca ? ubicacionGuardada() : null
  // «Mas cerca» sin saber donde esta la persona no se pide: se ordena por recomendados hasta que la comparta.
  const orden: OrdenDeTexto = ordenPedido === 'Cercania' && !punto ? 'Recomendados' : ordenPedido
  const [avisoUbicacion, setAvisoUbicacion] = useState<string>()
  const [pidiendo, setPidiendo] = useState(false)

  const zonas = catalogo.datos?.zonas
  const zonaElegida = zonas?.find((z) => String(z.id) === zona)
  const grupos = useMemo(() => agruparPorRegion(zonas ?? []), [zonas])

  useTitulo(q !== '' ? capitalizar(q) : zonaElegida ? `Gente en ${etiquetaDeZona(zonaElegida)}` : 'Buscar')

  // Con la ubicacion puesta y el orden «cerca» se mide desde la persona; sin ella no se manda nada.
  const criterios: CriteriosDeTexto = {
    q, orden,
    ...(cerca && punto ? { lat: punto.lat, lon: punto.lon } : {}),
    // El radio solo viaja con el punto: sin ubicacion no hay desde donde medir.
    ...(cerca && punto && radio !== null ? { radio } : {}),
    ...(zona !== '' ? { zona: Number(zona) } : {}),
    ...(soloVerificados ? { verificados: true } : {}),
    ...(cuatroEstrellas ? { calificacion: 4 } : {}),
  }
  const clave = JSON.stringify(criterios)

  const [acumulado, setAcumulado] = useState<Acumulado | null>(null)
  const [cargandoMas, setCargandoMas] = useState(false)

  useEffect(() => {
    // `vigente` descarta la respuesta de una busqueda que ya no es la pedida: la lenta no pisa a la rapida.
    let vigente = true
    const pedido: CriteriosDeTexto = JSON.parse(clave)
    buscarTexto(pedido)
      .then((p) => { if (vigente) setAcumulado(desdePagina(clave, p)) })
      .catch((e: unknown) => {
        if (vigente) setAcumulado({
          clave, resultados: [], total: 0, pagina: 1, hayMas: false, ordenAplicado: orden, aviso: null,
          error: e instanceof ErrorApi ? e.mensaje : 'No pudimos buscar. Intenta de nuevo.',
        })
      })
    return () => { vigente = false }
    // `orden` solo se usa para el mensaje de error: la busqueda depende de `clave`, que ya lo incluye.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [clave])

  const actual = acumulado !== null && acumulado.clave === clave ? acumulado : null
  const cargando = actual === null

  function cambiar(cambios: Record<string, string | null>) {
    const siguiente = new URLSearchParams(params)
    for (const [k, v] of Object.entries(cambios)) {
      if (v === null || v === '') siguiente.delete(k)
      else siguiente.set(k, v)
    }
    setParams(siguiente, { replace: true })
  }

  function pedirUbicacion(alTener: () => void) {
    if (!navigator.geolocation) {
      setAvisoUbicacion('Tu navegador no permite usar la ubicación.')
      return
    }
    setPidiendo(true)
    setAvisoUbicacion(undefined)
    navigator.geolocation.getCurrentPosition(
      (posicion) => {
        const p = { lat: posicion.coords.latitude, lon: posicion.coords.longitude }
        guardarUbicacion(p)
        setPidiendo(false)
        alTener()
      },
      () => {
        // Flujo 4A de CU-08: si la persona la niega no se insiste. Solo se pide al pulsar un boton.
        setAvisoUbicacion('No pudimos usar tu ubicación. Puedes elegir una comuna en vez de eso.')
        setPidiendo(false)
      },
      { timeout: 10_000, maximumAge: 60_000 },
    )
  }

  function elegirOrden(nuevo: OrdenDeTexto) {
    if (nuevo === 'Cercania' && !punto) {
      pedirUbicacion(() => cambiar({ orden: 'Cercania', cerca: '1' }))
      return
    }
    cambiar({ orden: nuevo === 'Recomendados' ? null : nuevo })
  }

  function quitarUbicacion() {
    olvidarUbicacion()
    // El radio se va con la ubicacion: sin punto no se puede medir, y dejarlo en la URL haria que
    // volviera a aplicarse en cuanto la persona comparta la ubicacion otra vez, sin haberlo pedido.
    cambiar({ cerca: null, radio: null, orden: orden === 'Cercania' ? null : orden })
  }

  async function verMas() {
    if (actual === null || !actual.hayMas) return
    setCargandoMas(true)
    try {
      const p = await buscarTexto({ ...(JSON.parse(clave) as CriteriosDeTexto), pagina: actual.pagina + 1 })
      setAcumulado(desdePagina(clave, p, actual.resultados))
    } catch (e) {
      setAcumulado({ ...actual, error: e instanceof ErrorApi ? e.mensaje : 'No pudimos cargar más.' })
    } finally {
      setCargandoMas(false)
    }
  }

  const titulo = q !== ''
    ? `«${q}»`
    : zonaElegida ? `Gente en ${etiquetaDeZona(zonaElegida)}` : 'Toda la comunidad'

  return (
    <div className="pagina">
      <section className="pagina-cabeza pagina-cabeza--buscar">
        <div className="contenedor">
          <p className="sobretitulo">Buscar</p>
          <h1 className="pagina-titulo pagina-titulo--buscar">{titulo}</h1>
          {/* La `key` hace que la barra se reinicie con el texto cuando cambia por Atras. */}
          <BarraDeBusqueda
            key={q} valorInicial={q} variante="pagina"
            alEnviar={(texto) => cambiar({ q: texto === '' ? null : texto })}
          />
        </div>
      </section>

      <div className="contenedor buscar-cuerpo">
        <div className="orden-barra" role="group" aria-label="Ordenar por">
          {ORDENES.map((o) => (
            <button
              key={o.valor} type="button" className="chip chip--orden" aria-pressed={actual?.ordenAplicado === o.valor || (actual === null && orden === o.valor)}
              disabled={pidiendo && o.valor === 'Cercania'}
              onClick={() => elegirOrden(o.valor)}
            >
              {o.valor === 'Recomendados' ? <Icono nombre="estrella" /> : o.valor === 'Cercania' ? <Icono nombre="pin" /> : null}
              {pidiendo && o.valor === 'Cercania' ? 'Buscando tu ubicación…' : o.texto}
            </button>
          ))}
        </div>

        {/* Los filtros van en SU grupo. Estaban dentro de «Ordenar por», y un lector de pantalla
            anunciaba «Solo verificados», la comuna y «Quitar mi ubicación» como formas de ordenar.
            Comparte el estilo de la fila de distancia (`radio-barra`), que es justo lo que hace falta:
            otra fila de chips 12 px mas abajo. Una clase propia la pondra quien lleva index.css. */}
        <div className="orden-barra radio-barra" role="group" aria-label="Filtrar los resultados">
          <button
            type="button" className="chip" aria-pressed={soloVerificados}
            onClick={() => cambiar({ verificados: soloVerificados ? null : '1' })}
            aria-describedby="nota-credencial"
          >
            <Icono nombre="sello" />Solo verificados
          </button>
          <button
            type="button" className="chip" aria-pressed={cuatroEstrellas}
            onClick={() => cambiar({ cal: cuatroEstrellas ? null : '4' })}
            aria-describedby="nota-promedio"
          >
            <Icono nombre="estrella" />4 estrellas o más
          </button>

          <label className="orden-zona">
            <span className="solo-lector">Comuna</span>
            <select value={zona} onChange={(e) => cambiar({ zona: e.target.value })} aria-label="Comuna">
              <option value="">Todo Chile</option>
              {grupos.map((g) => (
                <optgroup key={g.region} label={g.region}>
                  {g.comunas.flatMap(({ comuna, barrios }) => [
                    <option key={comuna.id} value={comuna.id}>{comuna.nombre}</option>,
                    ...barrios.map((b) => <option key={b.id} value={b.id}>{`${comuna.nombre} · ${b.nombre}`}</option>),
                  ])}
                </optgroup>
              ))}
            </select>
          </label>

          {punto && (
            <button type="button" className="chip" onClick={quitarUbicacion}>
              <Icono nombre="cerrar" />Quitar mi ubicación
            </button>
          )}
        </div>

        {/* Lo que estos dos filtros hacen, en texto visible y no en un `title`: con el dedo no hay
            `hover`, asi que en un telefono esa explicacion no existia. Van tambien como
            `aria-describedby` de su propio boton, para que el lector de pantalla las lea ahi. */}
        <p className="letra-chica" id="nota-promedio">
          El promedio se muestra desde la tercera reseña, así que este filtro solo cuenta a quienes ya tienen tres.
        </p>
        <p className="letra-chica" id="nota-credencial">
          Verificamos títulos y certificaciones una por una, así que todavía somos pocos los que tenemos el sello.
        </p>

        {/* Sin ubicacion no se muestra: preguntar «hasta que distancia» a quien no ha dicho desde
            donde no se puede responder, y un control que no hace nada es peor que no tenerlo. */}
        {punto && (
          <div className="radio-barra" role="group" aria-label="¿Hasta qué distancia?">
            <span className="radio-titulo">¿Hasta qué distancia?</span>
            {RADIOS.map((km) => (
              <button
                key={km} type="button" className="chip chip--radio"
                aria-pressed={radio === km}
                onClick={() => cambiar({ radio: radio === km ? null : String(km) })}
              >
                {km} km
              </button>
            ))}
            <button
              type="button" className="chip chip--radio" aria-pressed={radio === null}
              onClick={() => cambiar({ radio: null })}
            >
              Todo Chile
            </button>
          </div>
        )}

        {avisoUbicacion && <p className="aviso-linea" role="status">{avisoUbicacion}</p>}
        {catalogo.error && (
          <p className="aviso-linea" role="alert">
            {catalogo.error}{' '}
            <button type="button" className="enlace" onClick={catalogo.reintentar}>Reintentar</button>
          </p>
        )}

        {/* Este bloque NO es una region viva. Lo era entero, y cada cambio de filtro hacia que el
            lector de pantalla leyera de nuevo resultados, avisos y la lista completa. Lo unico que hay
            que anunciar es el recuento, y `aria-live` esta ahi; los avisos y errores ya traen su
            propio `role` de estado o de alerta. */}
        <div>
          {cargando && <p className="cargando-linea" role="status"><span className="pulso" aria-hidden="true" />Buscando…</p>}
          {actual?.error && <p className="aviso-linea" role="alert">{actual.error}</p>}
          {actual?.aviso && <p className="aviso-linea" role="status">{actual.aviso}</p>}

          {actual && !actual.error && actual.resultados.length === 0 && (
            <div className="vacio">
              <h2 className="vacio-titulo">Todavía no encontramos a nadie para {titulo}.</h2>
              <p>
                {radio !== null
                  ? `Estamos mirando solo a ${radio} km de ti. Prueba más lejos, o con otra palabra.`
                  : soloVerificados || cuatroEstrellas ? 'Prueba quitando los filtros: «4 estrellas» solo cuenta a quienes ya tienen 3 reseñas.' : zona !== '' ? 'Prueba en todo Chile, o con otra palabra.' : 'Prueba con otra palabra: un oficio («abogado»), un servicio («divorcio») o un nombre.'}
              </p>
              <p className="vacio-acciones">
                {/* Si hay radio puesto, es lo primero que hay que soltar: el resto de los filtros
                    pueden estar bien y la persona solo esta buscando demasiado cerca. */}
                {radio !== null && radio < RADIO_MAXIMO && (
                  <button type="button" className="boton" onClick={() => cambiar({ radio: String(RADIO_MAXIMO) })}>
                    Ampliar a {RADIO_MAXIMO} km
                  </button>
                )}
                {radio !== null && <button type="button" className="boton" onClick={() => cambiar({ radio: null })}>Buscar sin límite de distancia</button>}
                {(soloVerificados || cuatroEstrellas) && <button type="button" className="boton" onClick={() => cambiar({ verificados: null, cal: null })}>Quitar los filtros</button>}
                {zona !== '' && <button type="button" className="boton" onClick={() => cambiar({ zona: null })}>Buscar en todo Chile</button>}
                <Link to="/profesionales" className="enlace">¿Eres de este oficio? Súmate</Link>
              </p>
            </div>
          )}

          {actual && actual.resultados.length > 0 && (
            <>
              {/* El `h2` que faltaba entre el `h1` de la pagina y el `h3` de cada tarjeta: con el salto,
                  quien navega por encabezados pasaba de la pagina a una persona sin nada en medio. */}
              <h2 className="resultados-cuenta" aria-live="polite">
                <strong>{titulo}</strong> · {personas(actual.total)}
                {' · '}{ORDENES.find((o) => o.valor === actual.ordenAplicado)?.texto.toLowerCase()}
              </h2>
              <ul className="tarjetas-rejilla" aria-label="Resultados de la búsqueda">
                {actual.resultados.map((r) => (
                  <li key={r.perfilId}>
                    <TarjetaPersona p={{
                      slug: r.slug,
                      nombre: r.nombrePublico,
                      rubro: r.rubro,
                      zona: r.zonaPrincipal,
                      distancia: r.distancia,
                      precioDesde: r.precioDesde,
                      verificada: r.tieneCredencialVerificada,
                      detalle: r.totalResenas > 0 ? reputacion(r.promedioCalificacion, r.totalResenas) : null,
                      descripcion: r.descripcion,
                    }} />
                  </li>
                ))}
              </ul>
              {actual.hayMas && (
                <p className="seccion-cierre">
                  <button type="button" className="boton" disabled={cargandoMas} onClick={() => void verMas()}>
                    {cargandoMas ? 'Cargando…' : `Ver más (${actual.total - actual.resultados.length})`}
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
