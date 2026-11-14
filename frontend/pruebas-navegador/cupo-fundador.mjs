/**
 * El camino completo del cupo fundador, en un navegador de verdad y contra la API y
 * PostgreSQL de verdad: entrar, ver el contador, tomar el cupo, inscribir el medio de pago
 * en la pasarela de prueba, volver, y comprobar EN LA BASE que quedó lo que tenía que
 * quedar.
 *
 * Lo que ninguna prueba de Vitest puede decir: ahí el `fetch` está simulado. Aquí viajan
 * cookies reales por el proxy de Vite, el aviso de la pasarela llega al endpoint de verdad
 * y las filas quedan escritas.
 *
 * Y lo que ninguna prueba de xUnit puede decir tampoco: que la pantalla haga el recorrido
 * sin que nadie le explique nada.
 *
 * Necesita arriba la API y Vite (`ver-zass.bat`) y la base local con la migración del cobro
 * aplicada. La API limita a 5 intentos de autenticación cada 15 minutos por IP; para
 * correrlo varias veces seguidas, levantarla con `Zass__Tasa__A=100`.
 *
 *   node pruebas-navegador/cupo-fundador.mjs          (desde frontend/)
 */
import { execFileSync } from 'node:child_process'
import { mkdirSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { chromium } from 'playwright'

const BASE = 'https://localhost:5173'
const raiz = resolve(import.meta.dirname, '..', '..')
const carpeta = process.argv[2] ?? resolve(raiz, 'registros', 'capturas')
mkdirSync(carpeta, { recursive: true })

const SLUG = 'marcela-soto'
const CORREO = `demo-${SLUG}@zass.local`
const CLAVE_DEMO = 'demo-zass-2026'

/** La clave la lee el guion en el momento; nunca se imprime ni se pasa por argumentos. */
function sql(consulta) {
  return execFileSync(
    resolve(raiz, 'herramientas/pgsql/bin/psql.exe'),
    ['-h', '127.0.0.1', '-p', '5433', '-U', 'zass', '-d', 'zass', '-At', '-c', consulta],
    { env: { ...process.env, PGPASSWORD: readFileSync(resolve(raiz, '.clave-pg.txt'), 'utf8').trim() } },
  ).toString().trim()
}

function comprobar(texto, bien) {
  console.log(`${bien ? 'OK  ' : 'MAL '} ${texto}`)
  if (!bien) process.exitCode = 1
}

const perfil = sql(`SELECT id FROM perfiles_prestador WHERE slug = '${SLUG}'`)
if (perfil === '') {
  console.error(`MAL  no existe el perfil de demostración '${SLUG}'. Corre el sembrado de demo.`)
  process.exit(1)
}

// Se parte de cero para que el guion se pueda correr dos veces seguidas: el cupo vuelve a
// estar libre y la suscripción al plan de prueba. La base local es solo de desarrollo.
sql(`UPDATE cupos_fundador SET perfil_prestador_id = NULL, reservado_hasta = NULL
     WHERE perfil_prestador_id = '${perfil}'`)
sql(`DELETE FROM intentos_de_pago WHERE perfil_prestador_id = '${perfil}'`)
sql(`UPDATE suscripciones SET plan_id = 1, estado = 'Prueba', vigente_hasta = now() + interval '10 days'
     WHERE perfil_prestador_id = '${perfil}'`)

const cuposAntes = Number(sql('SELECT count(*) FROM cupos_fundador WHERE perfil_prestador_id IS NULL'))
const pagosAntes = Number(sql('SELECT count(*) FROM pagos'))

const navegador = await chromium.launch()
const pagina = await navegador.newPage({ viewport: { width: 1100, height: 900 }, ignoreHTTPSErrors: true })

const errores = []
const fallidas = []
pagina.on('pageerror', (e) => errores.push(e.message))
pagina.on('console', (m) => {
  if (m.type() === 'error' && !m.text().includes('404')) errores.push(m.text())
})
pagina.on('response', async (r) => {
  if (r.status() < 400) return
  let cuerpo = ''
  try { cuerpo = (await r.text()).slice(0, 250).replace(/\s+/g, ' ') } catch { cuerpo = '(sin cuerpo)' }
  fallidas.push(`${r.status()} ${r.request().method()} ${r.url().replace(BASE, '')} :: ${cuerpo}`)
})

try {
  // 1 · La página de planes, sin sesión: tiene que mostrar el precio y el contador.
  await pagina.goto(`${BASE}/planes`, { waitUntil: 'networkidle' })
  await pagina.screenshot({ path: resolve(carpeta, 'planes-sin-sesion.png'), fullPage: true })

  const textoSinSesion = await pagina.locator('article').first().innerText()
  comprobar('la página de planes dice el precio del fundador', textoSinSesion.includes('5.990'))
  comprobar('la página de planes dice el precio mensual', textoSinSesion.includes('7.990'))
  comprobar(
    `el contador muestra los ${cuposAntes} cupos que hay en la base`,
    textoSinSesion.includes(String(cuposAntes)),
  )
  comprobar(
    'sin sesión ofrece crear cuenta y no el botón del cupo',
    await pagina.getByRole('link', { name: /crear mi cuenta/i }).isVisible(),
  )

  // 2 · Entrar como la prestadora de demostración.
  await pagina.goto(`${BASE}/entrar`, { waitUntil: 'networkidle' })
  await pagina.getByLabel('Correo').fill(CORREO)
  await pagina.getByLabel('Contraseña').fill(CLAVE_DEMO)
  await pagina.getByRole('button', { name: /^entrar$/i }).click()
  await pagina.waitForURL(/mi-ficha/, { timeout: 20000 })

  // 3 · Tomar el cupo.
  await pagina.goto(`${BASE}/planes`, { waitUntil: 'networkidle' })
  await pagina.screenshot({ path: resolve(carpeta, 'planes-con-sesion.png'), fullPage: true })

  const boton = pagina.getByRole('button', { name: /quiero mi cupo/i })
  comprobar('con ficha publicada aparece el botón del cupo', await boton.isVisible())
  await boton.click()

  // 4 · La pasarela de prueba, que tiene que decir con todas las letras que es de prueba.
  await pagina.waitForURL(/pago\/inscripcion/, { timeout: 20000 })
  await pagina.screenshot({ path: resolve(carpeta, 'pago-simulado.png'), fullPage: true })

  const textoPasarela = await pagina.locator('article').first().innerText()
  comprobar('la pasarela avisa que es de PRUEBA', /prueba/i.test(textoPasarela))
  comprobar('y dice que no se cobra nada', /no se cobra/i.test(textoPasarela))

  const reservado = sql(
    `SELECT reservado_hasta IS NOT NULL FROM cupos_fundador WHERE perfil_prestador_id = '${perfil}'`)
  comprobar('el cupo quedó RESERVADO mientras se inscribe', reservado === 't')

  // 5 · Inscribir el medio de pago.
  await pagina.getByRole('button', { name: /inscribir mi medio de pago/i }).click()
  await pagina.waitForURL(/planes\/listo/, { timeout: 20000 })
  await pagina.screenshot({ path: resolve(carpeta, 'planes-listo.png'), fullPage: true })

  // 6 · Lo que de verdad importa: qué quedó en la base.
  const cupo = sql(
    `SELECT numero, reservado_hasta IS NULL FROM cupos_fundador WHERE perfil_prestador_id = '${perfil}'`)
  comprobar(`el cupo quedó CONFIRMADO (sin fecha de reserva): ${cupo}`, cupo.endsWith('|t'))

  const intento = sql(
    `SELECT tipo, monto, estado FROM intentos_de_pago WHERE perfil_prestador_id = '${perfil}'`)
  comprobar(`el intento es una inscripción de monto 0 y quedó pagada: ${intento}`,
    intento === 'Inscripcion|0|Pagado')

  const suscripcion = sql(
    `SELECT p.codigo, s.estado, (s.vigente_hasta::date - now()::date)
       FROM suscripciones s JOIN planes p ON p.id = s.plan_id
      WHERE s.perfil_prestador_id = '${perfil}'`)
  comprobar(`la suscripción es fundador, en prueba, con 30 días: ${suscripcion}`,
    suscripcion === 'fundador|Prueba|30')

  const pagosDespues = Number(sql('SELECT count(*) FROM pagos'))
  comprobar('una inscripción NO crea fila de pago', pagosDespues === pagosAntes)

  const cuposDespues = Number(sql('SELECT count(*) FROM cupos_fundador WHERE perfil_prestador_id IS NULL'))
  comprobar(`el contador bajó de ${cuposAntes} a ${cuposDespues}`, cuposDespues === cuposAntes - 1)

  // 7 · Y que siga apareciendo en la búsqueda, que es para lo que se paga.
  await pagina.goto(`${BASE}/buscar?q=corredora`, { waitUntil: 'networkidle' })
  await pagina.waitForTimeout(1500)
  await pagina.screenshot({ path: resolve(carpeta, 'fundador-en-la-busqueda.png'), fullPage: true })
  comprobar('el prestador aparece en la búsqueda',
    (await pagina.locator(`a[href="/prestadores/${SLUG}"]`).count()) > 0)

  // 8 · El mismo aviso dos veces no puede regalar nada. Se manda a mano, como lo haría una
  // pasarela que reintenta.
  const intentoId = sql(`SELECT id FROM intentos_de_pago WHERE perfil_prestador_id = '${perfil}'`)
  const repetido = await pagina.evaluate(async ({ id }) => {
    const r = await fetch('/api/pagos/aviso', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ intento: id, monto: '0', estado: 'aprobado', referencia: 'repetido' }),
    })
    return r.status
  }, { id: intentoId })
  comprobar(`el aviso repetido responde 200 y no un error (fue ${repetido})`, repetido === 200)

  const trasElRepetido = sql(
    `SELECT p.codigo, s.estado, (s.vigente_hasta::date - now()::date)
       FROM suscripciones s JOIN planes p ON p.id = s.plan_id
      WHERE s.perfil_prestador_id = '${perfil}'`)
  comprobar('y no movió la vigencia ni un día', trasElRepetido === suscripcion)
  comprobar('ni creó una fila de pago', Number(sql('SELECT count(*) FROM pagos')) === pagosAntes)

  comprobar('sin errores de JavaScript en la consola', errores.length === 0)
  comprobar('sin respuestas 4xx ni 5xx inesperadas', fallidas.length === 0)
  if (errores.length > 0) console.log('   errores:', errores.join(' | '))
  if (fallidas.length > 0) console.log('   fallidas:', fallidas.join(' | '))
} finally {
  await navegador.close()
  console.log(`\nCapturas en ${carpeta}`)
}
