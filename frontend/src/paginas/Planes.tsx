import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import { Icono } from '../marco/Icono'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'
import {
  CUPOS_FUNDADOR_TOTALES, type CatalogoDePlanes, type Cotizacion, type Plan,
  cotizar, pesos, planes as pedirPlanes, tomarCupoFundador,
} from '../funciones/planes/servicio'
import { useSesion } from '../sesion/contexto'

/*
 * La página de planes. Es la única pantalla pública donde se dice cuánto cuesta ZASS!, y el
 * contador de cupos es el gancho del lanzamiento: un enlace que se manda por WhatsApp y
 * donde se ve el número bajando.
 *
 * TEXTOS PROPUESTOS, pendientes de aprobación de Pablo (CONTRIBUIR.md, regla 12).
 */

const CODIGO_FUNDADOR = 'fundador'
const CODIGO_MENSUAL = 'mensual'

/** Lo que se le ofrece a quien no puede tomar el cupo todavía. */
type Puerta = 'puede' | 'sin-sesion' | 'sin-ficha'

function puertaDe(estado: string, roles: string[] | undefined): Puerta {
  if (estado !== 'dentro') return 'sin-sesion'
  return roles?.includes('prestador') === true ? 'puede' : 'sin-ficha'
}

function Cupos({ quedan }: { quedan: number }) {
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
      Quedan <strong>{quedan}</strong> de {CUPOS_FUNDADOR_TOTALES} cupos de fundador
    </p>
  )
}

function TarjetaDePlan({
  plan, destacado, cerrado, children,
}: {
  plan: Plan
  destacado: boolean
  cerrado: boolean
  children?: React.ReactNode
}) {
  return (
    <article
      className={`plan-tarjeta${destacado ? ' plan-tarjeta--destacada' : ''}`}
      aria-labelledby={`plan-${plan.codigo}`}
    >
      <h2 id={`plan-${plan.codigo}`}>{plan.nombre}</h2>
      <p className="plan-precio">
        <strong>{pesos(plan.precioMensual)}</strong> al mes
      </p>
      {plan.descripcion !== null && <p className="plan-descripcion">{plan.descripcion}</p>}
      <ul className="plan-incluye">
        <li>Hasta {plan.maxServicios} servicios en tu ficha</li>
        <li>Agenda de horas y reseñas</li>
        {plan.permiteCredenciales && <li>Credenciales en tu ficha</li>}
      </ul>
      {cerrado
        ? <p className="plan-cerrado">Cerrado: ya no quedan cupos.</p>
        : children}
    </article>
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
  const navegar = useNavigate()

  const [catalogo, setCatalogo] = useState<CatalogoDePlanes | null>(null)
  const [cargando, setCargando] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [codigoCupon, setCodigoCupon] = useState('')
  const [cotizacion, setCotizacion] = useState<Cotizacion | null>(null)
  const [errorDelCupon, setErrorDelCupon] = useState<string | null>(null)
  const [cotizando, setCotizando] = useState(false)

  const [tomando, setTomando] = useState(false)
  const [errorAlTomar, setErrorAlTomar] = useState<string | null>(null)

  /**
   * CC-04. Una sola clave por visita a esta pantalla, y la misma en cada reintento: es lo
   * que hace que dos clics no tomen dos cupos. Si se generara dentro del manejador del
   * clic, cada clic traería una distinta y no protegería de nada.
   */
  const clave = useRef(crypto.randomUUID()).current

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

  const { fundador, mensual } = useMemo(() => ({
    fundador: catalogo?.planes.find((p) => p.codigo === CODIGO_FUNDADOR) ?? null,
    mensual: catalogo?.planes.find((p) => p.codigo === CODIGO_MENSUAL) ?? null,
  }), [catalogo])

  async function aplicarCupon(evento: React.FormEvent) {
    evento.preventDefault()
    const codigo = codigoCupon.trim()
    if (codigo.length === 0) return

    setCotizando(true)
    setErrorDelCupon(null)
    try {
      setCotizacion(await cotizar(CODIGO_MENSUAL, codigo))
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

  async function tomarElCupo() {
    if (tomando) return
    setTomando(true)
    setErrorAlTomar(null)
    try {
      const intento = await tomarCupoFundador(clave)
      if (intento.urlParaPagar === null) {
        setErrorAlTomar('No pudimos abrir el pago. Vuelve a intentarlo.')
        return
      }
      // La pasarela puede ser nuestra pantalla de prueba o el sitio del banco.
      if (intento.urlParaPagar.startsWith('/')) navegar(intento.urlParaPagar)
      else window.location.assign(intento.urlParaPagar)
    } catch (e) {
      // 409: alguien se llevó el último cupo mientras mirabas. Se vuelve a preguntar para
      // que la pantalla deje de ofrecer algo que ya no existe.
      if (e instanceof ErrorApi && e.estado === 409) {
        setErrorAlTomar(e.mensaje)
        void traer()
      } else {
        setErrorAlTomar(e instanceof ErrorApi
          ? e.mensaje
          : 'No pudimos abrir el pago. Vuelve a intentarlo.')
      }
    } finally {
      setTomando(false)
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
        <Cupos quedan={quedan} />
      </header>

      <div className="planes-rejilla">
        {fundador !== null && (
          <TarjetaDePlan plan={fundador} destacado cerrado={cerrado}>
            {puerta === 'puede' && (
              <>
                <button
                  type="button"
                  className="boton boton--amarillo boton--lleno"
                  onClick={() => void tomarElCupo()}
                  disabled={tomando}
                >
                  <Icono nombre="flecha" />
                  {tomando ? 'Abriendo el pago…' : 'Quiero mi cupo'}
                </button>
                <p className="plan-letra-chica">
                  Inscribes tu medio de pago hoy y no se te cobra nada: tu primer mes es
                  gratis. Después, {pesos(fundador.precioMensual)} al mes mientras mantengas
                  tu suscripción.
                </p>
              </>
            )}
            {puerta === 'sin-sesion' && (
              <Link className="boton boton--amarillo boton--lleno" to="/crear-cuenta?volver=/planes">
                Crear mi cuenta
              </Link>
            )}
            {puerta === 'sin-ficha' && (
              <>
                <Link className="boton boton--amarillo boton--lleno" to="/ofrecer-servicios">
                  Publicar mi ficha primero
                </Link>
                <p className="plan-letra-chica">
                  El cupo se asigna a tu perfil de prestador, así que primero hay que crearlo.
                </p>
              </>
            )}
            {errorAlTomar !== null && (
              <p className="aviso aviso--error" role="alert">{errorAlTomar}</p>
            )}
          </TarjetaDePlan>
        )}

        {mensual !== null && (
          <TarjetaDePlan plan={mensual} destacado={false} cerrado={false}>
            <p className="plan-letra-chica">
              El plan de siempre. Tu primer mes también es gratis y lo puedes pausar cuando
              quieras.
            </p>
          </TarjetaDePlan>
        )}
      </div>

      {puerta === 'puede' && mensual !== null && (
        <section className="planes-cupon" aria-labelledby="cupon-titulo">
          <h2 id="cupon-titulo">¿Tienes un código de descuento?</h2>
          <form onSubmit={(e) => void aplicarCupon(e)}>
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
 * La vuelta del pago. NO confirma nada por su cuenta: lo que confirma el cupo es el aviso
 * que la pasarela le manda al servidor. Esta pantalla solo pregunta cómo quedó.
 */
export function PlanesListo() {
  useTitulo('Estamos confirmando tu cupo')
  // No tiene nada que hacer un buscador aquí: es la vuelta de un pago.
  useMeta({ noindex: true })

  return (
    <article className="pagina pagina--angosta">
      <h1>Estamos confirmando tu cupo</h1>
      <p className="bloque-texto">
        Tu medio de pago quedó inscrito y estamos esperando la confirmación. Puede tardar unos
        segundos.
      </p>
      <p className="bloque-texto">
        Cuando llegue, tu ficha pasa al plan fundador con el primer mes gratis. Lo puedes ver
        en <Link to="/mi-ficha">Mi ficha</Link>.
      </p>
    </article>
  )
}
