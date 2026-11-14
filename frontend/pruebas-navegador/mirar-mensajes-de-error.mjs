/**
 * Que ve una persona cuando la API falla. Corre el cliente HTTP de verdad en un navegador de
 * verdad y solo FALSIFICA la respuesta de la red, copiada tal cual de la que da la API:
 * asi se mide el camino real de api/cliente.ts, no una version simulada de el.
 *
 * Nace de dos cosas vistas en el navegador el 12-nov:
 *   - un 500 mostraba «An error occurred while processing your request.», en ingles, porque
 *     ese `title` lo escribe ASP.NET y el cliente lo prefería a su propio texto en castellano;
 *   - el limitador de peticiones responde con claves en castellano (`titulo`/`detalle`) en vez
 *     de las de RFC 7807 (`title`/`detail`), asi que el cliente NO puede leer su mensaje y la
 *     persona no se entera de que tiene que esperar.
 *
 * Necesita arriba la API y Vite.
 *
 *   node pruebas-navegador/mirar-mensajes-de-error.mjs     (desde frontend/)
 */
import { chromium } from 'playwright'

const BASE = 'https://localhost:5173'

let fallos = 0
function comprobar(texto, bien, detalle = '') {
  console.log(`${bien ? 'ok  ' : 'MAL '} ${texto}${detalle ? ` — ${detalle}` : ''}`)
  if (!bien) { fallos++; process.exitCode = 1 }
}

/** Lo que responde ASP.NET ante una excepcion no controlada. Copiado de una respuesta real. */
const QUINIENTOS = {
  type: 'https://tools.ietf.org/html/rfc9110#section-15.6.1',
  title: 'An error occurred while processing your request.',
  status: 500,
}
/**
 * Lo que responde el limitador (Zass.Api/Configuracion/Seguridad.cs, OnRejected).
 *
 * Los nombres son los de RFC 7807. Este arnes tenia `titulo`/`detalle`, copiados del servidor, y
 * eso ERA el defecto D7: con esos nombres el cliente no encuentra nada que mostrar y la persona
 * recibe «No se pudo completar la operación» en vez de «espera unos minutos». Se arreglaron las dos
 * puntas: el servidor manda `title`/`detail` (con una prueba de integracion que lo exige) y aqui se
 * comprueba que el cliente lo entiende.
 */
const CUATROCIENTOS_VEINTINUEVE = {
  type: 'https://tools.ietf.org/html/rfc9110#section-15.5.30',
  title: 'Demasiadas peticiones',
  detail: 'Espera unos minutos antes de volver a intentar.',
  status: 429,
  reintentarEn: 900,
}
/** La forma VIEJA, que no cumple el RFC. Se guarda para comprobar que no se vuelve a colar. */
const CUATROCIENTOS_VEINTINUEVE_VIEJO = {
  titulo: 'Demasiadas peticiones',
  detalle: 'Espera unos minutos antes de volver a intentar.',
  reintentarEn: 900,
}

const navegador = await chromium.launch()

async function loQueSeVe(cuerpo, estado) {
  const contexto = await navegador.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 900 }, locale: 'es-CL' })
  const pagina = await contexto.newPage()
  await pagina.route('**/api/buscar**', (ruta) =>
    ruta.fulfill({ status: estado, contentType: 'application/problem+json', body: JSON.stringify(cuerpo) }))
  await pagina.goto(`${BASE}/buscar?q=abogado`, { waitUntil: 'domcontentloaded' })
  await pagina.waitForTimeout(3000)
  const texto = await pagina.locator('body').innerText()
  await contexto.close()
  return texto
}

try {
  const con500 = await loQueSeVe(QUINIENTOS, 500)
  comprobar('ante un 500, NO se le muestra a la persona el texto en ingles de ASP.NET',
    !/An error occurred while processing your request/i.test(con500),
    /An error occurred while processing your request/i.test(con500) ? 'se muestra tal cual' : '')
  comprobar('ante un 500 se muestra el texto propio, en castellano',
    /El servidor tuvo un problema/i.test(con500),
    con500.split('\n').find((l) => /problema|error|servidor/i.test(l)) ?? '(no se encontro ningun aviso)')

  const con429 = await loQueSeVe(CUATROCIENTOS_VEINTINUEVE, 429)
  comprobar('ante un 429 se le dice a la persona que espere unos minutos',
    /espera unos minutos|minutos/i.test(con429),
    con429.split('\n').find((l) => /no se pudo|espera|minutos/i.test(l)) ?? '(no se encontro ningun aviso)')

  // Y la prueba de que el defecto era real: con los nombres viejos el aviso NO le llega a nadie. Si
  // algun dia el servidor vuelve a escribir `titulo`/`detalle`, la prueba de integracion lo caza;
  // esta de aqui explica por que importa.
  const con429Viejo = await loQueSeVe(CUATROCIENTOS_VEINTINUEVE_VIEJO, 429)
  comprobar('un 429 con los nombres viejos (titulo/detalle) NO le llega a la persona: era el defecto',
    !/espera unos minutos/i.test(con429Viejo), '')

  // --- Entrar bien pero no poder leer la cuenta -------------------------------------------
  // Visto de verdad el 12-nov con el limitador agotado: POST /api/auth/sesion devolvio 200 (la
  // persona QUEDO dentro, con sus cookies puestas) y el GET /api/mi/cuenta que viene detras dio
  // 429. Entrar.tsx hace `void recargar()` y Sesion.tsx se traga cualquier ErrorApi dejando
  // estado 'fuera', asi que `Ingresar` nunca recibe un error y no muestra nada: el boton
  // «Entrar» parece no hacer absolutamente nada. Aqui se reproduce sin depender del limitador.
  const ctx = await navegador.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 900 }, locale: 'es-CL' })
  const pagina = await ctx.newPage()
  await pagina.route('**/api/auth/sesion', (ruta) =>
    ruta.request().method() === 'POST'
      ? ruta.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ usuarioId: 'u1', roles: [], correoVerificado: true }) })
      : ruta.continue())
  await pagina.route('**/api/mi/cuenta', (ruta) =>
    ruta.fulfill({ status: 429, contentType: 'application/problem+json', body: JSON.stringify(CUATROCIENTOS_VEINTINUEVE) }))
  await pagina.goto(`${BASE}/entrar`, { waitUntil: 'networkidle' })
  await pagina.getByLabel('Correo').fill('alguien@zass.local')
  await pagina.getByLabel('Contraseña').fill('una-clave-bien-larga')
  await pagina.getByRole('button', { name: /^entrar$/i }).click()
  await pagina.waitForTimeout(5000)
  const sigueEnEntrar = new URL(pagina.url()).pathname === '/entrar'
  const hayAviso = (await pagina.getByRole('alert').count()) > 0
  comprobar('si se entra pero no se puede leer la cuenta, la pantalla lo DICE en vez de no hacer nada',
    !sigueEnEntrar || hayAviso,
    sigueEnEntrar && !hayAviso
      ? 'se queda en /entrar sin ningun aviso: el boton parece no hacer nada'
      : '')
  await ctx.close()
} catch (e) {
  console.log('MAL  se detuvo:', e.message.split('\n').slice(0, 5).join(' | '))
  process.exitCode = 1
} finally {
  console.log(`--- comprobaciones en rojo: ${fallos} ---`)
  await navegador.close()
}
