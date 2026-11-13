import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ErrorApi, obtener, enviar, olvidarCsrf } from './cliente'

/** Respuesta de fetch armada a mano, para no depender de un servidor. */
function respuesta(estado: number, cuerpo: unknown, tipo = 'application/json') {
  return new Response(cuerpo === null ? null : JSON.stringify(cuerpo), {
    status: estado,
    headers: { 'content-type': tipo },
  })
}

/** Ejecuta la llamada esperando que falle, y entrega el ErrorApi resultante. */
async function fallaDe(llamada: Promise<unknown>): Promise<ErrorApi> {
  let capturado: unknown
  let hubo = false
  try {
    await llamada
  } catch (e) {
    capturado = e
    hubo = true
  }
  if (!hubo) throw new Error('se esperaba un fallo y la llamada salio bien')
  return capturado as ErrorApi
}

let fetchFalso: ReturnType<typeof vi.fn>

beforeEach(() => {
  olvidarCsrf()
  fetchFalso = vi.fn()
  vi.stubGlobal('fetch', fetchFalso)
})

describe('obtener', () => {
  it('manda las cookies de sesion', async () => {
    fetchFalso.mockResolvedValueOnce(respuesta(200, { nombre: 'Pablo' }))

    await obtener('/api/mi/cuenta')

    const [, opciones] = fetchFalso.mock.calls[0]
    expect(opciones.credentials).toBe('include')
  })

  it('devuelve el cuerpo ya convertido', async () => {
    fetchFalso.mockResolvedValueOnce(respuesta(200, { nombre: 'Pablo' }))

    const cuenta = await obtener<{ nombre: string }>('/api/mi/cuenta')

    expect(cuenta.nombre).toBe('Pablo')
  })

  it('no pide token CSRF: un GET no lo necesita', async () => {
    fetchFalso.mockResolvedValueOnce(respuesta(200, {}))

    await obtener('/api/mi/cuenta')

    expect(fetchFalso).toHaveBeenCalledTimes(1)
    expect(fetchFalso.mock.calls[0][0]).toBe('/api/mi/cuenta')
  })
})

describe('enviar', () => {
  it('pide el token CSRF y lo manda en la cabecera', async () => {
    fetchFalso
      .mockResolvedValueOnce(respuesta(200, { token: 'ficha-123' }))
      .mockResolvedValueOnce(respuesta(200, { usuarioId: 'u1' }))

    await enviar('/api/mi/contrasena', { contrasenaActual: 'a', contrasenaNueva: 'b' })

    expect(fetchFalso.mock.calls[0][0]).toBe('/api/auth/csrf')
    const [, opciones] = fetchFalso.mock.calls[1]
    expect(opciones.headers['X-CSRF-Token']).toBe('ficha-123')
    expect(opciones.method).toBe('POST')
  })

  it('reutiliza el token: no lo pide dos veces', async () => {
    fetchFalso
      .mockResolvedValueOnce(respuesta(200, { token: 'ficha-123' }))
      .mockResolvedValueOnce(respuesta(200, {}))
      .mockResolvedValueOnce(respuesta(200, {}))

    await enviar('/api/uno', {})
    await enviar('/api/dos', {})

    const pedidosDeToken = fetchFalso.mock.calls.filter((c) => c[0] === '/api/auth/csrf')
    expect(pedidosDeToken).toHaveLength(1)
  })

  it('acepta otro metodo cuando se lo piden', async () => {
    fetchFalso
      .mockResolvedValueOnce(respuesta(200, { token: 'f' }))
      .mockResolvedValueOnce(respuesta(204, null))

    await enviar('/api/mi/cuenta', undefined, 'DELETE')

    expect(fetchFalso.mock.calls[1][1].method).toBe('DELETE')
  })

  it('devuelve undefined ante un 204 sin cuerpo', async () => {
    fetchFalso
      .mockResolvedValueOnce(respuesta(200, { token: 'f' }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))

    await expect(enviar('/api/algo', {})).resolves.toBeUndefined()
  })
})

// Este es el bloque que importa. El token antifalsificacion queda atado a la
// identidad de la sesion: el que se pidio ANTES de iniciar sesion deja de servir
// DESPUES, y la API contesta 403. Sin esto, el primer clic tras entrar falla y el
// mensaje no dice nada util.
describe('el token CSRF caduca al cambiar la sesion', () => {
  it('ante un 403 pide un token nuevo y reintenta una vez', async () => {
    fetchFalso
      .mockResolvedValueOnce(respuesta(200, { token: 'ficha-vieja' }))
      .mockResolvedValueOnce(respuesta(403, { title: 'Peticion no verificada' }, 'application/problem+json'))
      .mockResolvedValueOnce(respuesta(200, { token: 'ficha-nueva' }))
      .mockResolvedValueOnce(respuesta(200, { ok: true }))

    const r = await enviar<{ ok: boolean }>('/api/mi/contrasena', {})

    expect(r.ok).toBe(true)
    expect(fetchFalso.mock.calls[1][1].headers['X-CSRF-Token']).toBe('ficha-vieja')
    expect(fetchFalso.mock.calls[3][1].headers['X-CSRF-Token']).toBe('ficha-nueva')
  })

  it('no reintenta sin fin: si el segundo intento tambien da 403, falla', async () => {
    fetchFalso
      .mockResolvedValueOnce(respuesta(200, { token: 'a' }))
      .mockResolvedValueOnce(respuesta(403, { title: 'Peticion no verificada' }, 'application/problem+json'))
      .mockResolvedValueOnce(respuesta(200, { token: 'b' }))
      .mockResolvedValueOnce(respuesta(403, { title: 'Peticion no verificada' }, 'application/problem+json'))

    await expect(enviar('/api/algo', {})).rejects.toBeInstanceOf(ErrorApi)
    expect(fetchFalso).toHaveBeenCalledTimes(4)
  })
})

describe('errores', () => {
  it('convierte un problem+json en ErrorApi con estado y detalle', async () => {
    fetchFalso.mockResolvedValueOnce(
      respuesta(409, { title: 'Conflicto', detail: 'Ese correo ya tiene cuenta.' },
        'application/problem+json'))

    const fallo = await fallaDe(obtener('/api/algo'))

    expect(fallo).toBeInstanceOf(ErrorApi)
    expect(fallo.estado).toBe(409)
    expect(fallo.mensaje).toBe('Ese correo ya tiene cuenta.')
  })

  it('separa los errores por campo de una validacion 400', async () => {
    fetchFalso.mockResolvedValueOnce(
      respuesta(400, {
        title: 'Datos invalidos',
        errors: { Contrasena: ['Usa al menos 10 caracteres.'], Correo: ['No parece un correo.'] },
      }, 'application/problem+json'))

    const fallo = await fallaDe(obtener('/api/algo'))

    expect(fallo.campos).toEqual({
      contrasena: 'Usa al menos 10 caracteres.',
      correo: 'No parece un correo.',
    })
  })

  it('da un mensaje legible cuando la red falla, no un TypeError crudo', async () => {
    fetchFalso.mockRejectedValueOnce(new TypeError('Failed to fetch'))

    const fallo = await fallaDe(obtener('/api/algo'))

    expect(fallo).toBeInstanceOf(ErrorApi)
    expect(fallo.estado).toBe(0)
    expect(fallo.mensaje).toMatch(/conexi/i)
  })

  it('no se cae si el servidor responde 500 con HTML en vez de JSON', async () => {
    fetchFalso.mockResolvedValueOnce(
      new Response('<html>Error</html>', { status: 500, headers: { 'content-type': 'text/html' } }))

    const fallo = await fallaDe(obtener('/api/algo'))

    expect(fallo).toBeInstanceOf(ErrorApi)
    expect(fallo.estado).toBe(500)
  })
})

describe('renovar la sesion ante un 401', () => {
  it('renueva y repite la llamada una vez, y devuelve lo que salio', async () => {
    fetchFalso
      .mockResolvedValueOnce(respuesta(401, { title: 'No autorizado' }))   // la llamada
      .mockResolvedValueOnce(respuesta(200, { token: 'a' }))               // csrf
      .mockResolvedValueOnce(new Response(null, { status: 204 }))          // renovar
      .mockResolvedValueOnce(respuesta(200, { nombre: 'Pablo' }))          // la repeticion

    const cuenta = await obtener<{ nombre: string }>('/api/mi/cuenta')

    expect(cuenta.nombre).toBe('Pablo')
    const rutas = fetchFalso.mock.calls.map((c) => c[0])
    expect(rutas).toEqual(['/api/mi/cuenta', '/api/auth/csrf', '/api/auth/sesion/renovar', '/api/mi/cuenta'])
  })

  it('si no se puede renovar, deja el 401 original y no repite', async () => {
    fetchFalso
      .mockResolvedValueOnce(respuesta(401, { title: 'No autorizado' }))
      .mockResolvedValueOnce(respuesta(200, { token: 'a' }))
      .mockResolvedValueOnce(respuesta(401, { title: 'Sin sesion' }))      // renovar falla

    const e = await fallaDe(obtener('/api/mi/cuenta'))

    expect(e.estado).toBe(401)
    expect(fetchFalso).toHaveBeenCalledTimes(3)
  })

  it('no renueva en las rutas de autenticacion: un 401 al entrar es una clave mala', async () => {
    fetchFalso
      .mockResolvedValueOnce(respuesta(200, { token: 'a' }))
      .mockResolvedValueOnce(respuesta(401, { title: 'Credenciales invalidas' }))

    const e = await fallaDe(enviar('/api/auth/sesion', { correo: 'a', contrasena: 'b' }))

    expect(e.estado).toBe(401)
    expect(fetchFalso.mock.calls.map((c) => c[0])).not.toContain('/api/auth/sesion/renovar')
  })

  it('tres llamadas que reciben 401 a la vez renuevan UNA sola vez', async () => {
    let renovaciones = 0
    fetchFalso.mockImplementation(async (ruta: string) => {
      if (ruta === '/api/auth/csrf') return respuesta(200, { token: 'a' })
      if (ruta === '/api/auth/sesion/renovar') {
        renovaciones++
        return new Response(null, { status: 204 })
      }
      // La primera vez de cada ruta falla; despues de renovar, sale bien.
      return renovaciones === 0 ? respuesta(401, {}) : respuesta(200, { ok: true })
    })

    await Promise.all([obtener('/api/mi/a'), obtener('/api/mi/b'), obtener('/api/mi/c')])

    expect(renovaciones).toBe(1)
  })

  it('despues de renovar pide un token CSRF nuevo para el siguiente cambio', async () => {
    fetchFalso
      .mockResolvedValueOnce(respuesta(200, { token: 'viejo' }))   // csrf para el POST
      .mockResolvedValueOnce(respuesta(401, {}))                   // el POST falla por 401
      .mockResolvedValueOnce(new Response(null, { status: 204 }))  // renovar (usa el csrf ya guardado)
      .mockResolvedValueOnce(respuesta(200, { token: 'nuevo' }))   // csrf nuevo tras olvidarlo
      .mockResolvedValueOnce(new Response(null, { status: 204 }))  // el POST repetido

    await enviar('/api/mi/perfil')

    const repetido = fetchFalso.mock.calls[4][1]
    expect(repetido.headers['X-CSRF-Token']).toBe('nuevo')
  })
})

describe('enviar con cabeceras propias', () => {
  it('manda la Idempotency-Key junto al token CSRF', async () => {
    fetchFalso
      .mockResolvedValueOnce(respuesta(200, { token: 'csrf-1' }))
      .mockResolvedValueOnce(respuesta(201, { id: 'c1' }))

    await enviar('/api/citas', { bloqueId: 'b1' }, 'POST', { 'Idempotency-Key': 'intencion-1' })

    const [, opciones] = fetchFalso.mock.calls[1]
    expect(opciones.headers['Idempotency-Key']).toBe('intencion-1')
    expect(opciones.headers['X-CSRF-Token']).toBe('csrf-1')
  })

  it('repite la MISMA clave cuando reintenta por un 403 de CSRF', async () => {
    fetchFalso
      .mockResolvedValueOnce(respuesta(200, { token: 'viejo' }))
      .mockResolvedValueOnce(respuesta(403, { title: 'no verificada' }))
      .mockResolvedValueOnce(respuesta(200, { token: 'nuevo' }))
      .mockResolvedValueOnce(respuesta(201, { id: 'c1' }))

    await enviar('/api/citas', {}, 'POST', { 'Idempotency-Key': 'intencion-2' })

    const intentos = fetchFalso.mock.calls.filter(([ruta]) => ruta === '/api/citas')
    expect(intentos).toHaveLength(2)
    expect(intentos[0][1].headers['Idempotency-Key']).toBe('intencion-2')
    expect(intentos[1][1].headers['Idempotency-Key']).toBe('intencion-2')
    expect(intentos[1][1].headers['X-CSRF-Token']).toBe('nuevo')
  })

  it('no deja que una cabecera propia pise el token CSRF', async () => {
    fetchFalso
      .mockResolvedValueOnce(respuesta(200, { token: 'real' }))
      .mockResolvedValueOnce(respuesta(204, null))

    await enviar('/api/algo', {}, 'POST', { 'X-CSRF-Token': 'falso' })

    expect(fetchFalso.mock.calls[1][1].headers['X-CSRF-Token']).toBe('real')
  })

  it('sin cabeceras propias se comporta como siempre', async () => {
    fetchFalso
      .mockResolvedValueOnce(respuesta(200, { token: 't' }))
      .mockResolvedValueOnce(respuesta(204, null))

    await enviar('/api/algo', {})

    expect(fetchFalso.mock.calls[1][1].headers['Idempotency-Key']).toBeUndefined()
  })
})

// La API vive en Render con plan gratis y se duerme a los ~15 minutos. La PRIMERA accion del dia
// la despierta, y mientras despierta puede contestar un 502 con HTML o cortar la conexion. El
// token CSRF es lo primero que se pide en cualquier cambio de estado: si ahi se asume JSON, lo
// que llega a la pantalla es un SyntaxError, que no es ErrorApi, y la persona lee un generico.
describe('el token CSRF cuando la API todavia no esta despierta', () => {
  it('un 502 con HTML da un ErrorApi con su estado, no un error de sintaxis', async () => {
    fetchFalso.mockResolvedValueOnce(
      new Response('<html>502 Bad Gateway</html>', {
        status: 502, headers: { 'content-type': 'text/html' },
      }))

    const e = await fallaDe(enviar('/api/citas', { bloqueId: 'b1' }))

    expect(e).toBeInstanceOf(ErrorApi)
    expect(e.estado).toBe(502)
    expect(e.mensaje).toBe('El servidor tuvo un problema. Intenta de nuevo en un momento.')
    // Y el POST no se manda sin token: habria vuelto 403 y tapado el motivo real.
    expect(fetchFalso).toHaveBeenCalledTimes(1)
  })

  it('un fallo que SI viene en problem+json cuenta lo que dice la API', async () => {
    // El limite de peticiones (Tasa.*) contesta JSON con `detail`: ese texto es mejor que
    // cualquier generico, y solo se lee si se mira `r.ok` antes de buscar el token.
    fetchFalso.mockResolvedValueOnce(
      respuesta(429, { title: 'Demasiadas peticiones', detail: 'Espera un minuto y vuelve a intentar.' },
        'application/problem+json'))

    const e = await fallaDe(enviar('/api/citas', {}))

    expect(e.estado).toBe(429)
    expect(e.mensaje).toBe('Espera un minuto y vuelve a intentar.')
    expect(fetchFalso).toHaveBeenCalledTimes(1)
  })

  it('un 200 que no es JSON tampoco se toma por JSON', async () => {
    fetchFalso.mockResolvedValueOnce(
      new Response('<html>Esperando a la API</html>', {
        status: 200, headers: { 'content-type': 'text/html' },
      }))

    const e = await fallaDe(enviar('/api/citas', {}))

    expect(e).toBeInstanceOf(ErrorApi)
    expect(e.mensaje).toBe('El servidor tuvo un problema. Intenta de nuevo en un momento.')
    expect(fetchFalso).toHaveBeenCalledTimes(1)
  })

  it('no se cree un token que no venga en una respuesta JSON', async () => {
    // Lo que contesta mientras despierta no lo escribe la API: es la pagina de un intermediario.
    // Un token sacado de ahi no vale, y mandarlo solo cambiaria el 502 por un 403 incomprensible.
    fetchFalso.mockResolvedValueOnce(
      new Response(JSON.stringify({ token: 'de-una-pagina-cualquiera' }), {
        status: 200, headers: { 'content-type': 'text/html' },
      }))

    const e = await fallaDe(enviar('/api/citas', {}))

    expect(e).toBeInstanceOf(ErrorApi)
    expect(fetchFalso).toHaveBeenCalledTimes(1)
  })

  it('un JSON sin token es un fallo, no una cabecera que dice "undefined"', async () => {
    fetchFalso.mockResolvedValueOnce(respuesta(200, { mensaje: 'despertando' }))

    const e = await fallaDe(enviar('/api/citas', {}))

    expect(e).toBeInstanceOf(ErrorApi)
    expect(fetchFalso).toHaveBeenCalledTimes(1)
  })

  it('si la conexion se corta al pedir el token, el mensaje habla de la conexion', async () => {
    fetchFalso.mockRejectedValueOnce(new TypeError('Failed to fetch'))

    const e = await fallaDe(enviar('/api/citas', {}))

    expect(e).toBeInstanceOf(ErrorApi)
    expect(e.estado).toBe(0)
    expect(e.mensaje).toMatch(/conexi/i)
  })
})
