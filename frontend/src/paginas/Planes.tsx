import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import { fechaLarga } from '../funciones/perfil/estadoSuscripcion'
import { Icono } from '../marco/Icono'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'
import {
  CUPOS_FUNDADOR_TOTALES, type CatalogoDePlanes, type Cotizacion, type CupoTomado, type Plan,
  cotizar, inscribirTarjeta, iniciarCobro, pesos, planes as pedirPlanes, tomarCupoFundador,
} from '../funciones/planes/servicio'
import { Captcha, captchaActivo } from '../componentes/Captcha'
import { useSesion } from '../sesion/contexto'

/*
 * La página de planes. Es la única pantalla pública donde se dice cuánto cuesta ZASS!, y el
 * contador de cupos es el gancho del lanzamiento: un enlace que se manda por WhatsApp y
 * donde se ve el número bajando.
 *
 * TEXTOS PROPUESTOS, pendientes de aprobación de Pablo (CONTRIBUIR.md, regla 12).
 */

const CODIGO_FUNDADOR = 'fundador'

/** La hora en Chile, como se dice: «14:35». */
function hora(iso: string): string {
  return new Date(iso).toLocaleTimeString('es-CL', {
    hour: '2-digit', minute: '2-digit', hourCycle: 'h23', timeZone: 'America/Santiago',
  })
}
const CODIGO_ESENCIAL = 'mensual'
const CODIGO_PROFESIONAL = 'profesional'
const CODIGO_EQUIPO = 'equipo'
type CodigoContratable = typeof CODIGO_ESENCIAL | typeof CODIGO_PROFESIONAL

interface Caracteristica { texto: string, incluida: boolean }

/**
 * JQ-17. Lo que incluye un plan, LEÍDO de lo que manda la API: si mañana el Esencial gana las
 * estadísticas, la pantalla lo muestra sin tocar nada aquí.
 */
function caracteristicasDe(plan: Plan): Caracteristica[] {
  const tarjetas = plan.maxTarjetas === 1 ? '1 tarjeta NFC activa' : `${plan.maxTarjetas} tarjetas NFC activas`
  return [
    { texto: `Hasta ${plan.maxServicios} servicios en tu ficha`, incluida: true },
    { texto: tarjetas, incluida: true },
    { texto: 'Agenda en línea y reseñas', incluida: true },
    { texto: 'Credenciales verificadas', incluida: plan.permiteCredenciales },
    { texto: 'Estadísticas de tu ficha', incluida: plan.permiteEstadisticas },
    {
      texto: plan.maxFotosTrabajos > 0
        ? `Fotos de tus trabajos (hasta ${plan.maxFotosTrabajos})`
        : 'Fotos de tus trabajos',
      incluida: plan.maxFotosTrabajos > 0,
    },
    { texto: 'Verificación prioritaria', incluida: plan.verificacionPrioritaria },
  ]
}

/**
 * El plan Equipo NO viene de la API: está inactivo (todavía no se puede contratar) y el catálogo
 * solo devuelve los planes activos. Por eso va con datos FIJOS, solo para mostrarlo como
 * «Próximamente». Cuando se active, este bloque se borra y sale de la API como los otros dos.
 */
const EQUIPO_PROXIMAMENTE = {
  nombre: 'ZASS! Equipo',
  descripcion: 'Para estudios, consultas y talleres con varios profesionales.',
  precioMensual: 24990,
  caracteristicas: [
    { texto: 'Hasta 15 servicios por profesional', incluida: true },
    { texto: 'Hasta 10 tarjetas NFC', incluida: true },
    { texto: 'Hasta 5 profesionales con su propia ficha y agenda', incluida: true },
    { texto: 'Agenda en línea y reseñas', incluida: true },
    { texto: 'Credenciales verificadas', incluida: true },
    { texto: 'Estadísticas de tu ficha', incluida: true },
    { texto: 'Fotos de tus trabajos', incluida: true },
    { texto: 'Verificación prioritaria', incluida: true },
  ] satisfies Caracteristica[],
}

/** Lo que se le ofrece a quien no puede tomar el cupo todavía. */
type Puerta = 'puede' | 'sin-sesion' | 'sin-ficha'

function puertaDe(estado: string, roles: string[] | undefined): Puerta {
  if (estado !== 'dentro') return 'sin-sesion'
  return roles?.includes('prestador') === true ? 'puede' : 'sin-ficha'
}

function Cupos({
  quedan, precioProfesional, precioFundador,
}: { quedan: number, precioProfesional: number | null, precioFundador: number }) {
  if (quedan <= 0) {
    return (
      <p className="planes-cupos planes-cupos--cerrado" role="status">
        Los {CUPOS_FUNDADOR_TOTALES} cupos de fundador ya están tomados. El plan mensual sigue
        abierto.
      </p>
    )
  }
  return (
    <p className="planes-cupos" role="status">
      {precioProfesional !== null ? `El plan de ${pesos(precioProfesional)}, a` : 'El plan de lanzamiento, a'}
      {' '}{pesos(precioFundador)} para siempre. Quedan <strong>{quedan}</strong> de {CUPOS_FUNDADOR_TOTALES}.
    </p>
  )
}

function ListaDeCaracteristicas({ items }: { items: Caracteristica[] }) {
  return (
    <ul className="plan-incluye">
      {items.map((c) => (
        <li key={c.texto} className={c.incluida ? 'plan-item' : 'plan-item plan-item--no'}>
          <Icono nombre={c.incluida ? 'check' : 'cerrar'} />
          <span className="solo-lector">{c.incluida ? 'Incluye: ' : 'No incluye: '}</span>
          {c.texto}
        </li>
      ))}
    </ul>
  )
}

/** Una columna de la rejilla: `<section>` con su título, precio, lista y (si hay) acción. */
function ColumnaDePlan({
  codigo, nombre, descripcion, precio, items, destacada = false, proximamente = false, children,
}: {
  codigo: string
  nombre: string
  descripcion: string | null
  precio: number
  items: Caracteristica[]
  destacada?: boolean
  proximamente?: boolean
  children?: React.ReactNode
}) {
  const id = `plan-${codigo}`
  return (
    <section
      className={`plan-columna plan-columna--${codigo}${destacada ? ' plan-columna--destacada' : ''}`}
      aria-labelledby={id}
    >
      {destacada && <p className="plan-etiqueta">El más elegido</p>}
      {proximamente && <p className="plan-etiqueta plan-etiqueta--suave">Próximamente</p>}
      <h2 id={id}>{nombre}</h2>
      <p className="plan-precio">
        <strong>{pesos(precio)}</strong> al mes
      </p>
      {descripcion !== null && <p className="plan-descripcion">{descripcion}</p>}
      <ListaDeCaracteristicas items={items} />
      {children}
    </section>
  )
}

export function Planes() {
  useTitulo('Planes y precios')
  useMeta({
    descripcion:
      'Cuánto cuesta estar en ZASS!: el primer mes es gratis y los primeros 50 prestadores '
      + 'mantienen el precio de lanzamiento. Buscar es gratis, siempre.',
  })

  const { estado, usuario } = useSesion()

  const [catalogo, setCatalogo] = useState<CatalogoDePlanes | null>(null)
  const [cargando, setCargando] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [codigoCupon, setCodigoCupon] = useState('')
  const [cotizacion, setCotizacion] = useState<Cotizacion | null>(null)
  const [errorDelCupon, setErrorDelCupon] = useState<string | null>(null)
  const [cotizando, setCotizando] = useState(false)
  /** JQ-17: el plan que el cupón cotiza. Por defecto el Profesional, el que se destaca. */
  const [planElegido, setPlanElegido] = useState<CodigoContratable>(CODIGO_PROFESIONAL)
  const [contratando, setContratando] = useState<CodigoContratable | null>(null)
  const [errorAlContratar, setErrorAlContratar] = useState<string | null>(null)

  const [tomando, setTomando] = useState(false)
  const [errorAlTomar, setErrorAlTomar] = useState<string | null>(null)
  const [tomado, setTomado] = useState<CupoTomado | null>(null)
  // N3: captcha de un solo uso para «Quiero mi cupo», y la inscripción de la tarjeta que
  // confirma el cupo reservado.
  const [tokenCaptcha, setTokenCaptcha] = useState<string | null>(null)
  const [reinicioCaptcha, setReinicioCaptcha] = useState(0)
  const [abriendoTarjeta, setAbriendoTarjeta] = useState(false)
  const navegar = useNavigate()

  /**
   * CC-04. Una sola clave por visita a esta pantalla, y la misma en cada reintento: es lo
   * que hace que dos clics no tomen dos cupos. Si se generara dentro del manejador del
   * clic, cada clic traería una distinta y no protegería de nada.
   */
  const clave = useRef(crypto.randomUUID()).current
  /** CC-04 para la inscripción de la tarjeta (API-76): otra intención, otra clave. */
  const claveTarjeta = useRef(crypto.randomUUID()).current
  /** CC-04 para «Contratar»: una clave por plan, generada al montar (y repetida en los reintentos). */
  const clavesDeCobro = useRef<Record<CodigoContratable, string>>({
    [CODIGO_ESENCIAL]: crypto.randomUUID(),
    [CODIGO_PROFESIONAL]: crypto.randomUUID(),
  }).current

  /**
   * Pide el catálogo. No toca `cargando` al empezar a propósito: así el efecto que la llama
   * al montar no cambia estado de forma sincrónica (que es lo que el linter marca, y con
   * razón: provoca un render en cascada).
   */
  const traer = useCallback(async () => {
    try {
      setCatalogo(await pedirPlanes())
      setError(null)
    } catch (e) {
      setError(e instanceof ErrorApi
        ? e.mensaje
        : 'No pudimos cargar los planes. Vuelve a intentarlo.')
    } finally {
      setCargando(false)
    }
  }, [])

  /** El botón de reintentar: aquí sí se vuelve a mostrar «cargando», y viene de un clic. */
  const cargar = useCallback(() => {
    setCargando(true)
    setError(null)
    void traer()
  }, [traer])

  useEffect(() => { void traer() }, [traer])

  const puerta = puertaDe(estado, usuario?.roles)
  const quedan = catalogo?.cuposFundadorDisponibles ?? 0
  const cerrado = catalogo !== null && quedan <= 0

  const { fundador, esencial, profesional } = useMemo(() => ({
    fundador: catalogo?.planes.find((p) => p.codigo === CODIGO_FUNDADOR) ?? null,
    esencial: catalogo?.planes.find((p) => p.codigo === CODIGO_ESENCIAL) ?? null,
    profesional: catalogo?.planes.find((p) => p.codigo === CODIGO_PROFESIONAL) ?? null,
  }), [catalogo])

  async function aplicarCupon(evento: React.FormEvent) {
    evento.preventDefault()
    const codigo = codigoCupon.trim()
    if (codigo.length === 0) return

    setCotizando(true)
    setErrorDelCupon(null)
    try {
      setCotizacion(await cotizar(planElegido, codigo))
    } catch (e) {
      setCotizacion(null)
      // El código NO se borra: quien se equivocó en una letra lo corrige, no lo reescribe.
      setErrorDelCupon(e instanceof ErrorApi
        ? e.mensaje
        : 'No pudimos revisar ese código. Vuelve a intentarlo.')
    } finally {
      setCotizando(false)
    }
  }

  function elegirParaElCupon(codigo: CodigoContratable) {
    if (codigo === planElegido) return
    setPlanElegido(codigo)
    // La cotización de otro plan ya no vale: se vuelve a pedir con «Aplicar».
    setCotizacion(null)
    setErrorDelCupon(null)
  }

  /** JQ-17: abre el pago del plan. El cupón viaja SOLO si la cotización vigente es de ese plan. */
  async function contratar(codigo: CodigoContratable) {
    if (contratando !== null) return
    setContratando(codigo)
    setErrorAlContratar(null)
    const fallo = 'No pudimos abrir el pago. Vuelve a intentarlo.'
    const cupon = cotizacion !== null && cotizacion.codigoPlan === codigo
      ? cotizacion.cupon ?? undefined
      : undefined
    try {
      const intento = await iniciarCobro(codigo, clavesDeCobro[codigo], cupon)
      if (intento.urlParaPagar === null) {
        setErrorAlContratar(fallo)
        return
      }
      if (intento.urlParaPagar.startsWith('/')) navegar(intento.urlParaPagar)
      else window.location.assign(intento.urlParaPagar)
    } catch (e) {
      setErrorAlContratar(e instanceof ErrorApi ? e.mensaje : fallo)
    } finally {
      setContratando(null)
    }
  }

  /** La acción de una columna según quién mira (la misma puerta que ya usaba el cupo). */
  function accionDe(codigo: CodigoContratable) {
    if (puerta === 'puede') {
      return (
        <button
          type="button"
          // El ámbar es solo para la acción principal: la del plan que se destaca.
          className={codigo === CODIGO_PROFESIONAL ? 'boton boton--amarillo boton--lleno' : 'boton boton--lleno'}
          onClick={() => void contratar(codigo)}
          disabled={contratando !== null}
        >
          {contratando === codigo ? 'Abriendo el pago…' : 'Contratar'}
        </button>
      )
    }
    if (puerta === 'sin-sesion') {
      return (
        <Link className="boton boton--lleno" to="/crear-cuenta?volver=/planes">
          Quiero este plan
        </Link>
      )
    }
    return (
      <Link className="boton boton--lleno" to="/ofrecer-servicios">
        Quiero este plan
      </Link>
    )
  }

  async function tomarElCupo() {
    if (tomando) return
    if (captchaActivo() && tokenCaptcha === null) {
      setErrorAlTomar('Antes de tomar tu cupo, completa la verificación de que eres una persona.')
      return
    }
    setTomando(true)
    setErrorAlTomar(null)
    try {
      // N3 (22-nov-2026): el cupo queda RESERVADO 30 minutos; se confirma con la tarjeta.
      setTomado(await tomarCupoFundador(clave, tokenCaptcha))
      void traer()
    } catch (e) {
      // 409: alguien se llevó el último cupo mientras mirabas. Se vuelve a preguntar para
      // que la pantalla deje de ofrecer algo que ya no existe.
      if (e instanceof ErrorApi && e.estado === 409) {
        setErrorAlTomar(e.mensaje)
        void traer()
      } else {
        setErrorAlTomar(e instanceof ErrorApi
          ? e.mensaje
          : 'No pudimos tomar tu cupo. Vuelve a intentarlo.')
      }
    } finally {
      setTomando(false)
      // El token ya se gastó: si hay que reintentar, se pide otro.
      if (tokenCaptcha !== null) setReinicioCaptcha((n) => n + 1)
    }
  }

  /** API-76: lleva a Flow a inscribir la tarjeta que confirma el cupo reservado. */
  async function inscribirLaTarjeta() {
    if (abriendoTarjeta) return
    setAbriendoTarjeta(true)
    setErrorAlTomar(null)
    const fallo = 'No pudimos abrir la inscripción de la tarjeta. Vuelve a intentarlo.'
    try {
      const intento = await inscribirTarjeta(claveTarjeta)
      if (intento.urlParaPagar === null) {
        setErrorAlTomar(fallo)
        return
      }
      if (intento.urlParaPagar.startsWith('/')) navegar(intento.urlParaPagar)
      else window.location.assign(intento.urlParaPagar)
    } catch (e) {
      setErrorAlTomar(e instanceof ErrorApi ? e.mensaje : fallo)
    } finally {
      setAbriendoTarjeta(false)
    }
  }

  if (cargando) {
    return (
      <article className="pagina pagina--angosta">
        <h1>Planes y precios</h1>
        <p className="bloque-texto">Cargando los planes…</p>
      </article>
    )
  }

  if (error !== null || catalogo === null) {
    return (
      <article className="pagina pagina--angosta">
        <h1>Planes y precios</h1>
        <p className="aviso aviso--error" role="alert">{error ?? 'No pudimos cargar los planes.'}</p>
        <button type="button" className="boton boton--amarillo boton--lleno" onClick={cargar}>
          Volver a intentar
        </button>
      </article>
    )
  }

  return (
    <article className="pagina pagina--planes">
      <header className="planes-cabecera">
        <h1>Planes y precios</h1>
        <p className="planes-bajada">
          Quien busca no paga nunca, ni un peso. Pagas tú, por estar visible, y tu primer mes
          es gratis.
        </p>
      </header>

      {fundador !== null && (
        <section className="planes-fundador" aria-labelledby="fundador-titulo">
          <h2 id="fundador-titulo" className="solo-lector">Cupos de fundador</h2>
          <Cupos
            quedan={quedan}
            precioProfesional={profesional?.precioMensual ?? null}
            precioFundador={fundador.precioMensual}
          />
          {cerrado && <p className="plan-cerrado">Cerrado: ya no quedan cupos.</p>}
          {!cerrado && puerta === 'puede' && tomado !== null && tomado.reservadoHasta && (
            <div className="aviso aviso--atencion" role="status">
              <p>
                <strong>Tu cupo N° {tomado.numero} está reservado hasta las {hora(tomado.reservadoHasta)}.</strong>
                {' '}Para que quede tuyo, inscribe tu tarjeta en Flow antes de esa hora. Hoy no
                se te cobra nada: tu mes gratis empieza cuando la tarjeta queda inscrita.
              </p>
              <p>Si no la inscribes a tiempo, el cupo vuelve a quedar libre para otra persona.</p>
              <button
                type="button"
                className="boton boton--amarillo boton--lleno"
                onClick={() => void inscribirLaTarjeta()}
                disabled={abriendoTarjeta}
              >
                <Icono nombre="flecha" />
                {abriendoTarjeta ? 'Abriendo Flow…' : 'Inscribir mi tarjeta'}
              </button>
            </div>
          )}
          {!cerrado && puerta === 'puede' && tomado !== null && !tomado.reservadoHasta && (
            <div className="aviso aviso--atencion" role="status">
              <p>
                <strong>El cupo N° {tomado.numero} ya es tuyo.</strong>
                {tomado.mesGratisHasta && <> Tu mes gratis dura hasta el {fechaLarga(tomado.mesGratisHasta) ?? 'fin del mes'}.</>}
              </p>
            </div>
          )}
          {!cerrado && puerta === 'puede' && tomado === null && (
            <>
              <Captcha accion="cupo_fundador" alResolver={setTokenCaptcha} reinicio={reinicioCaptcha} />
              <button
                type="button"
                className="boton boton--amarillo boton--lleno"
                onClick={() => void tomarElCupo()}
                disabled={tomando}
              >
                <Icono nombre="flecha" />
                {tomando ? 'Reservando tu cupo…' : 'Quiero mi cupo'}
              </button>
              <p className="plan-letra-chica">
                Te guardamos el cupo 30 minutos para que inscribas tu tarjeta en Flow. Hoy no se
                te cobra nada: tu primer mes es gratis y empieza cuando la tarjeta queda
                inscrita. Después, {pesos(fundador.precioMensual)} al mes mientras mantengas tu
                suscripción.
              </p>
            </>
          )}
          {!cerrado && puerta === 'sin-sesion' && (
            <Link className="boton boton--amarillo boton--lleno" to="/crear-cuenta?volver=/planes">
              Crear mi cuenta
            </Link>
          )}
          {!cerrado && puerta === 'sin-ficha' && (
            <>
              <Link className="boton boton--amarillo boton--lleno" to="/ofrecer-servicios">
                Publicar mi ficha primero
              </Link>
              <p className="plan-letra-chica">
                El cupo se asigna a tu perfil de prestador, así que primero hay que crearlo.
              </p>
            </>
          )}
          {!cerrado && errorAlTomar !== null && (
            <p className="aviso aviso--error" role="alert">{errorAlTomar}</p>
          )}
        </section>
      )}

      <div className="planes-rejilla">
        {esencial !== null && (
          <ColumnaDePlan
            codigo={esencial.codigo}
            nombre={esencial.nombre}
            descripcion={esencial.descripcion}
            precio={esencial.precioMensual}
            items={caracteristicasDe(esencial)}
          >
            {accionDe(CODIGO_ESENCIAL)}
          </ColumnaDePlan>
        )}
        {profesional !== null && (
          <ColumnaDePlan
            codigo={profesional.codigo}
            nombre={profesional.nombre}
            descripcion={profesional.descripcion}
            precio={profesional.precioMensual}
            items={caracteristicasDe(profesional)}
            destacada
          >
            {accionDe(CODIGO_PROFESIONAL)}
          </ColumnaDePlan>
        )}
        <ColumnaDePlan
          codigo={CODIGO_EQUIPO}
          nombre={EQUIPO_PROXIMAMENTE.nombre}
          descripcion={EQUIPO_PROXIMAMENTE.descripcion}
          precio={EQUIPO_PROXIMAMENTE.precioMensual}
          items={EQUIPO_PROXIMAMENTE.caracteristicas}
          proximamente
        />
      </div>

      {errorAlContratar !== null && (
        <p className="aviso aviso--error" role="alert">{errorAlContratar}</p>
      )}

      {puerta === 'puede' && (esencial !== null || profesional !== null) && (
        <section className="planes-cupon" aria-labelledby="cupon-titulo">
          <h2 id="cupon-titulo">¿Tienes un código de descuento?</h2>
          <form onSubmit={(e) => void aplicarCupon(e)}>
            <fieldset className="cupon-plan">
              <legend>¿Para qué plan?</legend>
              {profesional !== null && (
                <label>
                  <input
                    type="radio" name="plan-del-cupon" checked={planElegido === CODIGO_PROFESIONAL}
                    onChange={() => elegirParaElCupon(CODIGO_PROFESIONAL)}
                  />
                  {profesional.nombre}
                </label>
              )}
              {esencial !== null && (
                <label>
                  <input
                    type="radio" name="plan-del-cupon" checked={planElegido === CODIGO_ESENCIAL}
                    onChange={() => elegirParaElCupon(CODIGO_ESENCIAL)}
                  />
                  {esencial.nombre}
                </label>
              )}
            </fieldset>
            <label htmlFor="cupon">Código de descuento</label>
            <input
              id="cupon"
              name="cupon"
              value={codigoCupon}
              onChange={(e) => setCodigoCupon(e.target.value)}
              autoComplete="off"
              maxLength={30}
            />
            <button type="submit" className="boton" disabled={cotizando}>
              {cotizando ? 'Revisando…' : 'Aplicar'}
            </button>
          </form>

          {errorDelCupon !== null && (
            <p className="aviso aviso--error" role="alert">{errorDelCupon}</p>
          )}

          {cotizacion !== null && (
            <dl className="cupon-desglose">
              <dt>Precio del plan</dt>
              <dd>{pesos(cotizacion.precioMensual)}</dd>
              <dt>Descuento{cotizacion.porcentajeDescuento !== null
                ? ` (${cotizacion.porcentajeDescuento} %)` : ''}</dt>
              <dd>−{pesos(cotizacion.descuento)}</dd>
              <dt>Pagas</dt>
              <dd><strong>{pesos(cotizacion.total)}</strong></dd>
            </dl>
          )}
        </section>
      )}

      <p className="planes-promesa">Ningún plan compra un mejor lugar en la búsqueda.</p>

      <footer className="planes-pie">
        <p>
          Buscar y contactar en ZASS! es gratis, siempre. Lo que se paga es aparecer, y lo
          paga quien ofrece el servicio.
        </p>
      </footer>
    </article>
  )
}

/**
 * La vuelta del pago (API-75). Esta pantalla NO confirma nada: la API ya le preguntó a la
 * pasarela con su firma y manda aquí con `?resultado=` y uno de cuatro valores fijos. Cualquier
 * otro valor (o ninguno) se trata como «revisar»: nunca se dice «listo» sin que la API lo diga.
 * «procesando» (22-nov-2026) es el pago que la pasarela todavía tiene pendiente: no se rechazó,
 * y el aviso de la pasarela lo cierra solo.
 * (Textos PROPUESTOS: los aprueba Pablo.)
 */
export function PlanesListo() {
  const [parametros] = useSearchParams()
  const resultado = parametros.get('resultado')
  const caso = resultado === 'aprobado' || resultado === 'rechazado' || resultado === 'procesando'
    ? resultado
    : 'revisar'

  const titulo = caso === 'aprobado'
    ? 'Listo: tu tarjeta quedó inscrita'
    : caso === 'rechazado'
      ? 'No pudimos inscribir tu tarjeta'
      : caso === 'procesando'
        ? 'Tu pago se está procesando'
        : 'Estamos confirmando tu tarjeta'
  useTitulo(titulo)
  // No tiene nada que hacer un buscador aquí: es la vuelta de un pago.
  useMeta({ noindex: true })

  return (
    <article className="pagina pagina--angosta">
      <h1>{titulo}</h1>
      {caso === 'aprobado' && (
        <p className="bloque-texto">
          Tu cupo de fundador queda asegurado. No se cobró nada: el primer cobro llega cuando
          termine tu mes gratis. Lo puedes ver en <Link to="/mi-ficha">Mi ficha</Link>.
        </p>
      )}
      {caso === 'rechazado' && (
        <>
          <p className="bloque-texto">
            La pasarela no aceptó la tarjeta y no se cobró nada. Tu cupo sigue siendo tuyo hasta
            que termine tu mes gratis. Algunos bancos no permiten guardar la tarjeta para pagos
            automáticos: prueba con otra.
          </p>
          <p className="bloque-texto">
            <Link className="boton" to="/mi-ficha">Probar con otra tarjeta</Link>
          </p>
        </>
      )}
      {caso === 'procesando' && (
        <>
          <p className="bloque-texto">
            La pasarela todavía no confirma tu pago. No lo intentes de nuevo: cuando se confirme,
            tu suscripción se actualiza sola.
          </p>
          <p className="bloque-texto">
            Lo puedes ver en <Link to="/mi-ficha">Mi ficha</Link>.
          </p>
        </>
      )}
      {caso === 'revisar' && (
        <>
          <p className="bloque-texto">
            Estamos esperando la confirmación de la pasarela. Puede tardar unos segundos.
          </p>
          <p className="bloque-texto">
            Cuando llegue, tu tarjeta queda inscrita y tu cupo asegurado. Lo puedes ver en
            {' '}<Link to="/mi-ficha">Mi ficha</Link>.
          </p>
        </>
      )}
    </article>
  )
}
