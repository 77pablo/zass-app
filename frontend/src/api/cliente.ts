/**
 * Cliente HTTP de ZASS!. Un solo lugar que sabe de cookies, token CSRF y de como
 * se ve un error de la API, para que ninguna pantalla lo repita a su manera.
 *
 * La API devuelve los errores en formato problem+json (RFC 7807), asi que aqui se
 * traducen a algo que un componente pueda mostrar sin pensar.
 */

export class ErrorApi extends Error {
  /** Codigo HTTP. Vale 0 cuando ni siquiera hubo respuesta (sin conexion). */
  readonly estado: number
  /** Texto base del error (sin el codigo de peticion). */
  readonly mensaje: string
  /** Errores por campo de una validacion, con el nombre del campo en minuscula. */
  readonly campos: Record<string, string>
  /**
   * El cuerpo tal como lo mando la API. Casi ninguna pantalla lo necesita, pero algunas
   * respuestas traen datos que hay que MOSTRAR y no caben en una frase: el 422 de CU-16 4E
   * señala los fragmentos del comentario que parecen datos personales. Vacio si no hubo cuerpo.
   */
  readonly datos: Record<string, unknown>
  /**
   * Identificador de la peticion (16 caracteres hexadecimales). Viene en el cuerpo del error
   * como `idPeticion` y en la cabecera `X-Id-Peticion`. Sirve para que el usuario lo dicte
   * a soporte y el equipo lo busque en los registros de Render.
   */
  readonly idPeticion: string | undefined

  constructor(
    estado: number, mensaje: string, campos: Record<string, string> = {},
    datos: Record<string, unknown> = {}, idPeticion?: string,
  ) {
    super(mensaje)
    this.name = 'ErrorApi'
    this.estado = estado
    this.mensaje = mensaje
    this.campos = campos
    this.datos = datos
    this.idPeticion = idPeticion
  }

  /**
   * Mensaje listo para mostrar en pantalla. Incluye el codigo de peticion como linea
   * secundaria SOLO cuando hay idPeticion (errores inesperados: 5xx, 429, sin conexion, etc.).
   * En errores de validacion (400 con campos) NO se incluye, porque el usuario ya sabe
   * que corregir y el codigo solo anadiria ruido.
   * Texto decidido por Pablo, 17-nov: «Código para soporte: <idPeticion>» (opción 1 de la DECISIÓN T4).
   */
  get paraMostrar(): string {
    if (!this.idPeticion) return this.mensaje
    return `${this.mensaje}\nCódigo para soporte: ${this.idPeticion}`
  }
}

const RUTA_CSRF = '/api/auth/csrf'

let ficha: string | null = null

/**
 * Borra el token guardado. Hay que llamarla al iniciar y al cerrar sesion: el
 * token esta atado a la identidad, y el de antes no sirve despues.
 */
export function olvidarCsrf(): void {
  ficha = null
}

async function tokenCsrf(renovar = false): Promise<string> {
  if (ficha !== null && !renovar) return ficha
  const r = await fetch(RUTA_CSRF, { credentials: 'include' })
  const cuerpo = (await r.json()) as { token: string }
  ficha = cuerpo.token
  return ficha
}

/** Lee la respuesta: devuelve el cuerpo, o lanza ErrorApi si no salio bien. */
async function leer<T>(r: Response): Promise<T> {
  if (!r.ok) throw await fallo(r)

  if (r.status === 204 || r.headers.get('content-length') === '0') {
    return undefined as T
  }
  const texto = await r.text()
  return (texto === '' ? undefined : JSON.parse(texto)) as T
}

async function fallo(r: Response): Promise<ErrorApi> {
  // Un 500 puede llegar como pagina HTML. Si se asume JSON, el error real queda
  // tapado por un "Unexpected token <" que no le dice nada a nadie.
  let cuerpo: Record<string, unknown> = {}
  try {
    cuerpo = (await r.json()) as Record<string, unknown>
  } catch {
    cuerpo = {}
  }

  const campos: Record<string, string> = {}
  const errores = cuerpo.errors as Record<string, string[]> | undefined
  if (errores) {
    for (const [campo, mensajes] of Object.entries(errores)) {
      if (mensajes?.length) campos[campo.toLowerCase()] = mensajes[0]
    }
  }

  // idPeticion: SOLO en errores inesperados (5xx, 429, error de red, etc.).
  // NO en errores de validacion (400 con errores por campo), porque ahi el usuario
  // ya sabe que corregir y el codigo solo anadiria ruido.
  const esValidacion = Object.keys(campos).length > 0
  const idPeticion = esValidacion
    ? undefined
    : (cuerpo.idPeticion as string | undefined) ?? r.headers.get('X-Id-Peticion') ?? undefined

  // El `title` de un 5xx no lo escribe nadie de este proyecto: lo pone ASP.NET y viene en
  // ingles de fabrica ("An error occurred while processing your request."). Mostrarlo tal cual
  // deja texto en ingles delante de una persona en una pantalla que es toda en castellano, y
  // ademas le cuenta de la maquina en vez de decirle que hacer. Para 5xx manda el texto propio;
  // `detail`, que si lo escribe la API cuando tiene algo util que decir, se sigue respetando.
  const mensaje =
    (cuerpo.detail as string | undefined) ??
    (r.status >= 500 ? undefined : (cuerpo.title as string | undefined)) ??
    generico(r.status)

  return new ErrorApi(r.status, mensaje, campos, cuerpo, idPeticion)
}

function generico(estado: number): string {
  if (estado >= 500) return 'El servidor tuvo un problema. Intenta de nuevo en un momento.'
  if (estado === 404) return 'No encontramos lo que buscabas.'
  return 'No se pudo completar la operación.'
}

async function llamar(ruta: string, opciones: RequestInit): Promise<Response> {
  try {
    return await fetch(ruta, opciones)
  } catch {
    // fetch solo rechaza cuando no hubo respuesta: sin red, servidor caido, DNS.
    throw new ErrorApi(0, 'No se pudo conectar. Revisa tu conexión e intenta de nuevo.')
  }
}

const RUTA_RENOVAR = '/api/auth/sesion/renovar'

// Una sola renovacion a la vez: si tres peticiones reciben 401 juntas, las tres esperan la
// misma promesa. Renovar tres veces gastaria el token de refresco de las otras dos.
let renovando: Promise<boolean> | null = null

/**
 * API-10. Pide un token de acceso nuevo con la cookie de refresco. Devuelve si se pudo.
 * El token de acceso dura poco a proposito; sin esto, quien deja la pestana abierta un rato
 * veria "no autorizado" en la primera accion, aunque su sesion siga siendo valida.
 */
function renovarSesion(): Promise<boolean> {
  renovando ??= (async () => {
    try {
      let r = await unIntento(RUTA_RENOVAR, undefined, 'POST', await tokenCsrf())
      if (r.status === 403) r = await unIntento(RUTA_RENOVAR, undefined, 'POST', await tokenCsrf(true))
      // El token CSRF esta atado a la identidad, y la renovacion la cambia.
      olvidarCsrf()
      return r.ok
    } catch {
      return false
    } finally {
      renovando = null
    }
  })()
  return renovando
}

/**
 * Si la respuesta es 401 y la ruta no es la propia autenticacion, renueva la sesion y repite
 * la llamada UNA vez. Las rutas de /api/auth/ quedan fuera: un 401 en el ingreso significa
 * "contrasena incorrecta", no "token vencido", y renovar ahi enmascararia el error real.
 */
async function conRenovacion(
  ruta: string, hacer: () => Promise<Response>,
): Promise<Response> {
  const r = await hacer()
  if (r.status !== 401 || ruta.startsWith('/api/auth/')) return r
  return (await renovarSesion()) ? hacer() : r
}

/** GET. No lleva token CSRF porque no cambia nada. */
export async function obtener<T>(ruta: string): Promise<T> {
  const r = await conRenovacion(ruta, () =>
    llamar(ruta, {
      method: 'GET',
      credentials: 'include',
      headers: { Accept: 'application/json' },
    }),
  )
  return leer<T>(r)
}

/**
 * Cualquier metodo que cambia estado. Adjunta el token CSRF y, si la API lo
 * rechaza con 403, pide uno nuevo y reintenta UNA vez.
 *
 * Ese reintento no es un parche: el token antifalsificacion queda atado a la
 * identidad de quien lo pidio, asi que el obtenido en la pantalla de ingreso
 * deja de valer en cuanto la persona entra. Sin el reintento, la primera accion
 * despues de iniciar sesion falla siempre, y con un mensaje que no se entiende.
 */
export async function enviar<T>(
  ruta: string,
  cuerpo?: unknown,
  metodo: 'POST' | 'PUT' | 'DELETE' = 'POST',
  /**
   * Cabeceras propias. La unica que hoy se usa es `Idempotency-Key` (CC-04). Se REPITEN tal cual
   * en cada reintento interno (el 403 de CSRF y el 401 de renovar sesion): son la misma
   * intencion, y una clave nueva por reintento haria que el servidor la tomara por otra.
   */
  cabeceras: Record<string, string> = {},
  /**
   * `keepalive`: que la peticion sobreviva si la pagina se va enseguida (un `tel:` o un
   * `mailto:` que abre otra aplicacion). Solo lo pide quien lo necesita; por defecto, no.
   */
  opciones: { keepalive?: boolean } = {},
): Promise<T> {
  const r = await conRenovacion(ruta, async () => {
    let intento = await unIntento(ruta, cuerpo, metodo, await tokenCsrf(), cabeceras, opciones)
    if (intento.status === 403) {
      intento = await unIntento(ruta, cuerpo, metodo, await tokenCsrf(true), cabeceras, opciones)
    }
    return intento
  })

  return leer<T>(r)
}

function unIntento(
  ruta: string, cuerpo: unknown, metodo: string, token: string,
  cabeceras: Record<string, string> = {},
  opciones: { keepalive?: boolean } = {},
): Promise<Response> {
  return llamar(ruta, {
    ...(opciones.keepalive ? { keepalive: true } : {}),
    method: metodo,
    credentials: 'include',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
      // Antes que las propias: nadie puede pisar el token CSRF desde fuera.
      ...cabeceras,
      'X-CSRF-Token': token,
    },
    ...(cuerpo === undefined ? {} : { body: JSON.stringify(cuerpo) }),
  })
}
