/**
 * Agendar una hora de verdad, con los DOS lados abiertos a la vez en el mismo navegador:
 * el prestador define su horario en «Mi agenda», y quien busca abre su ficha, elige servicio y
 * hora, confirma, y el prestador ve la solicitud en «Mis citas» y la confirma.
 *
 * De paso comprueba lo que solo se ve sin simular el fetch: la ficha SIN sesion esconde el
 * contacto (contactoRequiereCuenta) y CON sesion lo muestra.
 *
 * Monta su escenario sobre un prestador de demostracion (sembrar-demo.sql) porque crear uno
 * nuevo escribe en perfiles_prestador.ubicacion_exacta (geography) y eso necesita PostGIS. Todo
 * lo que toca lo deja como estaba: contrasena, rol, horario, bloques, citas y la cuenta creada.
 *
 * Necesita arriba la API (https://localhost:7277) y Vite (https://localhost:5173), y los datos
 * de pruebas-navegador/datos/sembrar-demo.sql.
 *
 *   node pruebas-navegador/mirar-agendar.mjs [carpeta-de-capturas]     (desde frontend/)
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
const CORREO_PRESTADOR = `demo-${SLUG}@zass.local`
/** Lo que sembrar-demo.sql deja: un hash que no es de ninguna contrasena, para que nadie entre. */
const HASH_DEMO = 'x'
const correoBuscador = `buscadora.${Date.now()}@zass.local`
const clave = 'una-clave-bien-larga'

function sql(consulta) {
  return execFileSync(
    resolve(raiz, 'herramientas/pgsql/bin/psql.exe'),
    ['-h', '127.0.0.1', '-p', '5433', '-U', 'zass', '-d', 'zass', '-At', '-c', consulta],
    {
      env: {
        ...process.env,
        PGPASSWORD: readFileSync(resolve(raiz, '.clave-pg.txt'), 'utf8').trim(),
        // Windows entrega los argumentos a psql.exe en la pagina de codigos ANSI, no en UTF-8:
        // sin esto, un acento en el SQL («Tasación») llega roto y psql corta con
        // «invalid byte sequence for encoding "UTF8"».
        PGCLIENTENCODING: 'WIN1252',
      },
    },
  ).toString().trim()
}

let fallos = 0
function comprobar(texto, bien, detalle = '') {
  console.log(`${bien ? 'ok  ' : 'MAL '} ${texto}${detalle ? ` — ${detalle}` : ''}`)
  if (!bien) { fallos++; process.exitCode = 1 }
}

const errores = []
const fallidas = []
function vigilar(pagina, quien) {
  pagina.on('pageerror', (e) => errores.push(`[${quien}] pageerror: ${e.message}`))
  pagina.on('console', (m) => { if (m.type() === 'error') errores.push(`[${quien}] ${m.text()}`) })
  pagina.on('requestfailed', (p) => fallidas.push(`[${quien}] ABORTADA ${p.method()} ${p.url()} -> ${p.failure()?.errorText}`))
  pagina.on('response', async (r) => {
    if (r.status() < 400) return
    let cuerpo = ''
    try { cuerpo = (await r.text()).slice(0, 250).replace(/\s+/g, ' ') } catch { cuerpo = '(sin cuerpo)' }
    fallidas.push(`[${quien}] ${r.status()} ${r.request().method()} ${r.url().replace(BASE, '')} :: ${cuerpo}`)
  })
}

const navegador = await chromium.launch()
const ctxBuscador = await navegador.newContext({ viewport: { width: 1280, height: 900 }, ignoreHTTPSErrors: true })
const ctxPrestador = await navegador.newContext({ viewport: { width: 1280, height: 900 }, ignoreHTTPSErrors: true })
const buscadora = await ctxBuscador.newPage()
const prestadora = await ctxPrestador.newPage()
vigilar(buscadora, 'buscadora')
vigilar(prestadora, 'prestadora')
const foto = (pagina, n) => pagina.screenshot({ path: resolve(carpeta, `agendar-${n}.png`), fullPage: true })

const idPrestador = sql(`SELECT id FROM perfiles_prestador WHERE slug = '${SLUG}'`)
const servicioId = sql(`SELECT ps.id FROM prestador_servicios ps JOIN servicios s ON s.id = ps.servicio_id
  WHERE ps.perfil_prestador_id = '${idPrestador}' AND s.nombre = 'Tasación comercial'`)

try {
  // --- 1. La ficha SIN sesion: el contacto no se entrega ---------------------------------
  const sinSesion = await (await navegador.newContext({ ignoreHTTPSErrors: true })).newPage()
  const respuesta = await sinSesion.request.get(`${BASE}/api/prestadores/${SLUG}`)
  const ficha = await respuesta.json()
  comprobar('sin sesion la API marca contactoRequiereCuenta',
    ficha.contactoRequiereCuenta === true, `contactoRequiereCuenta=${ficha.contactoRequiereCuenta}`)
  comprobar('sin sesion el contacto viene VACIO (ni telefono, ni WhatsApp, ni correo, ni sitio)',
    Object.values(ficha.contacto ?? {}).every((v) => v === null || v === undefined),
    JSON.stringify(ficha.contacto))
  await sinSesion.goto(`${BASE}/prestadores/${SLUG}`, { waitUntil: 'networkidle' })
  comprobar('sin sesion la pantalla pide crear cuenta para hablar',
    await sinSesion.getByRole('link', { name: /crear mi cuenta gratis/i }).isVisible())
  comprobar('sin sesion NO hay boton de WhatsApp',
    (await sinSesion.getByRole('link', { name: /WhatsApp/i }).count()) === 0)
  await foto(sinSesion, '01-ficha-sin-sesion')
  await sinSesion.context().close()

  // --- 2. Crear la cuenta de quien busca, en la pantalla de verdad ------------------------
  await buscadora.goto(`${BASE}/crear-cuenta`, { waitUntil: 'networkidle' })
  await buscadora.getByLabel('Nombre').fill('Rosa Buscadora')
  await buscadora.getByLabel('Correo').fill(correoBuscador)
  await buscadora.getByLabel('Contraseña').fill(clave)
  await buscadora.getByRole('button', { name: /crear cuenta/i }).click()
  await buscadora.getByRole('heading', { name: /revisa tu correo/i }).waitFor({ timeout: 20000 })
  comprobar('crear cuenta de buscadora responde «Revisa tu correo»', true)
  sql(`UPDATE usuarios SET correo_verificado = true WHERE correo::text = '${correoBuscador}'`)

  // --- 3. Montar el escenario: la prestadora de demostracion puede entrar ------------------
  // sembrar-demo.sql no le pone rol ni contrasena usable: se le prestan los de la cuenta recien
  // creada y al final se le devuelve el hash 'x' y se le quita el rol.
  sql(`UPDATE usuarios SET hash_contrasena = (SELECT hash_contrasena FROM usuarios WHERE correo::text = '${correoBuscador}')
       WHERE correo::text = '${CORREO_PRESTADOR}'`)
  sql(`INSERT INTO usuario_roles (usuario_id, rol_id, asignado_en)
       SELECT u.id, 2, now() FROM usuarios u WHERE u.correo::text = '${CORREO_PRESTADOR}'
       ON CONFLICT DO NOTHING`)

  await prestadora.goto(`${BASE}/entrar`, { waitUntil: 'networkidle' })
  await prestadora.getByLabel('Correo').fill(CORREO_PRESTADOR)
  await prestadora.getByLabel('Contraseña').fill(clave)
  await prestadora.getByRole('button', { name: /^entrar$/i }).click()
  await prestadora.waitForURL(/mi-ficha/, { timeout: 20000 })
  comprobar('la prestadora entra y cae en Mi ficha', true)
  await foto(prestadora, '02-mi-ficha-prestadora')
  comprobar('Mi ficha muestra su nombre publico',
    await prestadora.getByRole('heading', { level: 1, name: /Marcela Soto Rivas/ }).isVisible())

  // --- 4. Mi agenda: horario todos los dias, para que haya horas en los 7 dias visibles ----
  await prestadora.goto(`${BASE}/mi-agenda`, { waitUntil: 'networkidle' })
  await prestadora.getByRole('heading', { level: 1, name: 'Mi agenda' }).waitFor({ timeout: 20000 })
  const tarjeta = prestadora.locator('section.agenda-servicio', {
    has: prestadora.getByRole('heading', { name: 'Tasación comercial' }),
  })
  comprobar('Mi agenda lista los 3 servicios de la ficha',
    (await prestadora.locator('section.agenda-servicio').count()) === 3,
    `tarjetas=${await prestadora.locator('section.agenda-servicio').count()}`)

  for (let d = 0; d < 7; d++) {
    await tarjeta.getByRole('button', { name: 'Agregar horario' }).click()
    const n = d + 1
    await tarjeta.getByLabel(`Día, franja ${n}`).selectOption({ index: d })
    await tarjeta.getByLabel(`Desde, franja ${n}`).fill('09:00')
    await tarjeta.getByLabel(`Hasta, franja ${n}`).fill('18:00')
    await tarjeta.getByLabel(`Duración de cada hora, franja ${n}`).selectOption('60')
  }
  await tarjeta.getByRole('button', { name: 'Guardar horario' }).click()
  const aviso = tarjeta.getByText(/quedaron \d+ horas disponibles/i)
  await aviso.waitFor({ timeout: 30000 })
  const texto = (await aviso.textContent()).trim()
  comprobar('guardar el horario genera horas', Number(texto.match(/(\d+)/)?.[1] ?? 0) > 0, texto)
  await foto(prestadora, '03-mi-agenda-horario')

  const bloquesEnBase = sql(`SELECT count(*) FROM bloques WHERE prestador_servicio_id = '${servicioId}'`)
  comprobar('los bloques quedan en la base', Number(bloquesEnBase) > 0, `bloques=${bloquesEnBase}`)

  // --- 5. La ficha CON sesion: el contacto completo ---------------------------------------
  await buscadora.goto(`${BASE}/entrar`, { waitUntil: 'networkidle' })
  await buscadora.getByLabel('Correo').fill(correoBuscador)
  await buscadora.getByLabel('Contraseña').fill(clave)
  await buscadora.getByRole('button', { name: /^entrar$/i }).click()
  await buscadora.waitForURL((u) => !u.pathname.includes('entrar'), { timeout: 20000 })
  comprobar('la buscadora entra', true, `quedo en ${buscadora.url().replace(BASE, '')}`)

  const conSesion = await (await buscadora.request.get(`${BASE}/api/prestadores/${SLUG}`)).json()
  comprobar('con sesion la API ya no marca contactoRequiereCuenta',
    !conSesion.contactoRequiereCuenta, `contactoRequiereCuenta=${conSesion.contactoRequiereCuenta}`)
  comprobar('con sesion el contacto viene completo',
    Boolean(conSesion.contacto?.whatsapp && conSesion.contacto?.telefono && conSesion.contacto?.correo),
    JSON.stringify(conSesion.contacto))

  await buscadora.goto(`${BASE}/prestadores/${SLUG}`, { waitUntil: 'networkidle' })
  comprobar('con sesion la pantalla muestra el boton de WhatsApp',
    await buscadora.getByRole('link', { name: /Escribir por WhatsApp/i }).isVisible())
  await foto(buscadora, '04-ficha-con-sesion')

  // --- 6. Agendar: servicio, hora, confirmar ----------------------------------------------
  await buscadora.getByRole('button', { name: /Tasación comercial/ }).last().click()
  const horas = buscadora.locator('.agenda-horas button')
  await horas.first().waitFor({ timeout: 25000 })
  comprobar('el calendario ofrece horas libres', (await horas.count()) > 0, `horas=${await horas.count()}`)
  const horaTexto = await horas.first().textContent()
  await horas.first().click()
  await buscadora.getByRole('heading', { name: 'Tu hora' }).waitFor({ timeout: 10000 })
  await foto(buscadora, '05-hora-elegida')
  await buscadora.locator('.campo-nota textarea').fill('Vengo de una comprobación de navegador.')
  await buscadora.getByRole('button', { name: /Confirmar esta hora|Pedir esta hora/ }).click()
  await buscadora.getByRole('heading', { name: /Tu hora está confirmada|Pedimos tu hora/ }).waitFor({ timeout: 25000 })
  const codigo = (await buscadora.locator('.agenda-codigo').textContent()).trim()
  comprobar('agendar devuelve un codigo de cita', codigo.length > 0, `codigo=${codigo} hora=${horaTexto}`)
  await foto(buscadora, '06-cita-hecha')

  const enBase = sql(`SELECT estado FROM citas WHERE codigo = '${codigo}'`)
  comprobar('la cita existe en la base', enBase.length > 0, `estado=${enBase}`)

  // --- 7. Mis citas, de los dos lados ------------------------------------------------------
  await buscadora.goto(`${BASE}/mis-citas`, { waitUntil: 'networkidle' })
  await buscadora.getByRole('heading', { level: 1 }).waitFor({ timeout: 15000 })
  comprobar('la buscadora ve su cita en Mis citas',
    await buscadora.getByText(codigo).first().isVisible().catch(() => false))
  await foto(buscadora, '07-mis-citas-buscadora')

  await prestadora.goto(`${BASE}/mis-citas`, { waitUntil: 'networkidle' })
  await prestadora.getByRole('heading', { level: 1 }).waitFor({ timeout: 15000 })
  comprobar('la prestadora ve la solicitud en Mis citas',
    await prestadora.getByText(codigo).first().isVisible().catch(() => false))
  await foto(prestadora, '08-mis-citas-prestadora')

  const botonConfirmar = prestadora.getByRole('button', { name: /^Confirmar$/ })
  if (await botonConfirmar.count()) {
    await botonConfirmar.first().click()
    await prestadora.waitForTimeout(3000)
    const despues = sql(`SELECT estado FROM citas WHERE codigo = '${codigo}'`)
    comprobar('la prestadora puede confirmar la cita', despues === 'Confirmada', `estado=${despues}`)
    await foto(prestadora, '09-cita-confirmada')
  } else {
    comprobar('la cita ya nacio confirmada (modo automatico), no hay boton Confirmar', enBase === 'Confirmada', `estado=${enBase}`)
  }

  // --- 8. A ancho de telefono --------------------------------------------------------------
  for (const [pagina, quien, rutas] of [
    [buscadora, 'buscadora', [['10-movil-ficha', `/prestadores/${SLUG}`], ['11-movil-mis-citas', '/mis-citas']]],
    [prestadora, 'prestadora', [['12-movil-mi-agenda', '/mi-agenda']]],
  ]) {
    await pagina.setViewportSize({ width: 390, height: 844 })
    for (const [n, ruta] of rutas) {
      await pagina.goto(BASE + ruta, { waitUntil: 'networkidle' })
      await pagina.waitForTimeout(1200)
      const m = await pagina.evaluate(() => ({ doc: document.documentElement.scrollWidth, ven: window.innerWidth }))
      comprobar(`${ruta} (${quien}) a 390 px no desborda`, m.doc <= m.ven + 1, `${m.doc} vs ${m.ven}`)
      await foto(pagina, n)
    }
  }
} catch (e) {
  console.log('MAL  el recorrido se detuvo:', e.message.split('\n').slice(0, 6).join(' | '))
  await foto(buscadora, '98-error-buscadora').catch(() => {})
  await foto(prestadora, '99-error-prestadora').catch(() => {})
  process.exitCode = 1
} finally {
  console.log('\n--- errores de consola ---')
  for (const e of new Set(errores)) console.log('  ', e.slice(0, 250))
  console.log('--- peticiones fallidas ---')
  for (const f of new Set(fallidas)) console.log('  ', f.slice(0, 300))
  console.log(`--- comprobaciones en rojo: ${fallos} ---`)

  // Dejarlo todo como estaba: la cita, los bloques, el horario, el rol y el hash de demostracion.
  sql(`DELETE FROM cambios_cita WHERE cita_id IN (SELECT c.id FROM citas c JOIN bloques b ON b.id = c.bloque_horario_id WHERE b.prestador_servicio_id = '${servicioId}')`)
  sql(`DELETE FROM notificaciones WHERE cita_id IN (SELECT c.id FROM citas c JOIN bloques b ON b.id = c.bloque_horario_id WHERE b.prestador_servicio_id = '${servicioId}')`)
  sql(`DELETE FROM contactos WHERE cita_id IN (SELECT c.id FROM citas c JOIN bloques b ON b.id = c.bloque_horario_id WHERE b.prestador_servicio_id = '${servicioId}')`)
  sql(`DELETE FROM citas WHERE bloque_horario_id IN (SELECT id FROM bloques WHERE prestador_servicio_id = '${servicioId}')`)
  sql(`DELETE FROM bloques WHERE prestador_servicio_id = '${servicioId}'`)
  sql(`DELETE FROM disponibilidades WHERE prestador_servicio_id = '${servicioId}'`)
  sql(`DELETE FROM usuario_roles WHERE rol_id = 2 AND usuario_id = (SELECT id FROM usuarios WHERE correo::text = '${CORREO_PRESTADOR}')`)
  sql(`UPDATE usuarios SET hash_contrasena = '${HASH_DEMO}' WHERE correo::text = '${CORREO_PRESTADOR}'`)
  sql(`DELETE FROM usuarios WHERE correo::text = '${correoBuscador}'`)
  console.log('limpieza: cita, bloques, horario, rol prestado y cuenta de prueba borrados; hash de demostracion restaurado')
  await navegador.close()
}
