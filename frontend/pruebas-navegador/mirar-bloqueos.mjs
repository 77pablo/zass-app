/**
 * Bloquear fechas en «Mi agenda», lo unico de la agenda que nunca se habia visto en un navegador:
 * crear un bloqueo, verlo listado, comprobar que esas horas desaparecen del calendario publico, y
 * quitarlo para que vuelvan.
 *
 * Ademas separa el navegador de la aplicacion: los campos `type="date"` y `type="time"` los pinta
 * el navegador con SU idioma, no el del sitio. Se abre la misma pantalla en en-US y en es-CL para
 * saber si un «mm/dd/yyyy» es de la aplicacion o del arnes.
 *
 * Monta el escenario sobre un prestador de sembrar-demo.sql y lo deja todo como estaba.
 *
 *   node pruebas-navegador/mirar-bloqueos.mjs [carpeta-de-capturas]     (desde frontend/)
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
const HASH_DEMO = 'x'
const correoBuscador = `apoyo.${Date.now()}@zass.local`
const clave = 'una-clave-bien-larga'

function sql(consulta) {
  return execFileSync(
    resolve(raiz, 'herramientas/pgsql/bin/psql.exe'),
    ['-h', '127.0.0.1', '-p', '5433', '-U', 'zass', '-d', 'zass', '-At', '-c', consulta],
    {
      env: {
        ...process.env,
        PGPASSWORD: readFileSync(resolve(raiz, '.clave-pg.txt'), 'utf8').trim(),
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
const idPrestador = sql(`SELECT id FROM perfiles_prestador WHERE slug = '${SLUG}'`)
const servicios = sql(`SELECT string_agg(id::text, ',') FROM prestador_servicios WHERE perfil_prestador_id = '${idPrestador}'`)

const navegador = await chromium.launch()

/** '2026-11-20' sumando dias a hoy. */
function enDias(n) {
  return new Date(Date.now() + n * 86400000).toISOString().slice(0, 10)
}

try {
  // --- Escenario: una cuenta real presta su hash a la prestadora de demostracion -----------
  const registro = await (await navegador.newContext({ ignoreHTTPSErrors: true })).request
    .post(`${BASE}/api/auth/registro`, {
      data: { correo: correoBuscador, contrasena: clave, nombre: 'Apoyo de Prueba', rol: 'buscador' },
    })
  comprobar('la cuenta de apoyo se crea', registro.status() === 201, `HTTP ${registro.status()}`)
  sql(`UPDATE usuarios SET hash_contrasena = (SELECT hash_contrasena FROM usuarios WHERE correo::text = '${correoBuscador}')
       WHERE correo::text = '${CORREO_PRESTADOR}'`)
  sql(`INSERT INTO usuario_roles (usuario_id, rol_id, asignado_en)
       SELECT u.id, 2, now() FROM usuarios u WHERE u.correo::text = '${CORREO_PRESTADOR}' ON CONFLICT DO NOTHING`)

  // --- El mismo campo de fecha, en dos idiomas de navegador --------------------------------
  const formatos = {}
  for (const idioma of ['en-US', 'es-CL']) {
    const ctx = await navegador.newContext({ ignoreHTTPSErrors: true, locale: idioma, viewport: { width: 1280, height: 900 } })
    const p = await ctx.newPage()
    p.on('response', async (r) => {
      if (r.status() < 400) return
      let cuerpo = ''
      try { cuerpo = (await r.text()).slice(0, 200).replace(/\s+/g, ' ') } catch { cuerpo = '(sin cuerpo)' }
      fallidas.push(`[${idioma}] ${r.status()} ${r.request().method()} ${r.url().replace(BASE, '')} :: ${cuerpo}`)
    })
    await p.goto(`${BASE}/entrar`, { waitUntil: 'networkidle' })
    await p.getByLabel('Correo').fill(CORREO_PRESTADOR)
    await p.getByLabel('Contraseña').fill(clave)
    await p.getByRole('button', { name: /^entrar$/i }).click()
    await p.waitForURL(/mi-ficha/, { timeout: 20000 })
    await p.goto(`${BASE}/mi-agenda`, { waitUntil: 'networkidle' })
    await p.getByRole('heading', { level: 1, name: 'Mi agenda' }).waitFor({ timeout: 20000 })
    // El texto del marcador lo pinta el navegador; se lee del propio control.
    formatos[idioma] = await p.evaluate(() => {
      const fecha = document.querySelector('input[type="date"]')
      const hora = document.querySelector('input[type="time"]')
      return {
        fecha: fecha ? getComputedStyle(fecha).direction + '|' + (fecha.getAttribute('lang') ?? 'sin-lang') : 'no-hay',
        htmlLang: document.documentElement.lang,
        hayHora: Boolean(hora),
      }
    })
    await p.screenshot({ path: resolve(carpeta, `bloqueos-idioma-${idioma}.png`), fullPage: true })
    await ctx.close()
  }
  comprobar('la pagina declara el idioma del sitio en <html lang>',
    formatos['en-US'].htmlLang.startsWith('es'), `lang=${formatos['en-US'].htmlLang}`)
  console.log(`     (los campos date/time los pinta el navegador segun SU idioma: en-US=${JSON.stringify(formatos['en-US'])} es-CL=${JSON.stringify(formatos['es-CL'])})`)

  // --- Bloquear fechas, en un navegador en es-CL -------------------------------------------
  const contexto = await navegador.newContext({ ignoreHTTPSErrors: true, locale: 'es-CL', viewport: { width: 1280, height: 900 } })
  const pagina = await contexto.newPage()
  pagina.on('pageerror', (e) => errores.push(`pageerror: ${e.message}`))
  pagina.on('console', (m) => { if (m.type() === 'error') errores.push(m.text()) })
  pagina.on('requestfailed', (p) => fallidas.push(`ABORTADA ${p.method()} ${p.url()} -> ${p.failure()?.errorText}`))
  pagina.on('response', async (r) => {
    if (r.status() < 400) return
    let cuerpo = ''
    try { cuerpo = (await r.text()).slice(0, 250).replace(/\s+/g, ' ') } catch { cuerpo = '(sin cuerpo)' }
    fallidas.push(`${r.status()} ${r.request().method()} ${r.url().replace(BASE, '')} :: ${cuerpo}`)
  })

  await pagina.goto(`${BASE}/entrar`, { waitUntil: 'networkidle' })
  await pagina.getByLabel('Correo').fill(CORREO_PRESTADOR)
  await pagina.getByLabel('Contraseña').fill(clave)
  await pagina.getByRole('button', { name: /^entrar$/i }).click()
  await pagina.waitForURL(/mi-ficha/, { timeout: 20000 })

  // Un horario para el servicio, si no no hay horas que bloquear.
  await pagina.goto(`${BASE}/mi-agenda`, { waitUntil: 'networkidle' })
  const tarjeta = pagina.locator('section.agenda-servicio').first()
  const nombreServicio = (await tarjeta.getByRole('heading').first().textContent()).trim()
  for (let d = 0; d < 7; d++) {
    await tarjeta.getByRole('button', { name: 'Agregar horario' }).click()
    const n = d + 1
    await tarjeta.getByLabel(`Día, franja ${n}`).selectOption({ index: d })
    await tarjeta.getByLabel(`Desde, franja ${n}`).fill('09:00')
    await tarjeta.getByLabel(`Hasta, franja ${n}`).fill('13:00')
    await tarjeta.getByLabel(`Duración de cada hora, franja ${n}`).selectOption('60')
  }
  await tarjeta.getByRole('button', { name: 'Guardar horario' }).click()
  await tarjeta.getByText(/quedaron \d+ horas disponibles/i).waitFor({ timeout: 30000 })

  comprobar('antes de bloquear, la pantalla dice que no hay fechas bloqueadas',
    await pagina.getByText(/no tienes fechas bloqueadas/i).isVisible())

  const desde = enDias(5)
  const hasta = enDias(7)
  await pagina.getByLabel(/Desde el día/i).fill(desde)
  await pagina.getByLabel(/Hasta el día/i).fill(hasta)
  await pagina.getByLabel(/Motivo/i).fill('Vacaciones de prueba')
  await pagina.getByRole('button', { name: /^Bloquear$/ }).click()
  await pagina.waitForTimeout(2500)
  await pagina.screenshot({ path: resolve(carpeta, 'bloqueos-01-creado.png'), fullPage: true })

  const enBase = sql(`SELECT count(*) FROM bloqueos_agenda WHERE perfil_prestador_id = '${idPrestador}'`)
  comprobar('el bloqueo queda guardado en la base', Number(enBase) === 1, `bloqueos=${enBase}`)
  comprobar('el bloqueo aparece listado en la pantalla, sin recargar',
    await pagina.getByText(/^(El|Del) \d{2}-\d{2}-\d{4}/).first().isVisible().catch(() => false))
  comprobar('el motivo escrito se muestra',
    await pagina.getByText(/Vacaciones de prueba/).first().isVisible().catch(() => false))

  // --- Esas horas ya no se ofrecen en la ficha publica -------------------------------------
  const srv = sql(`SELECT ps.id FROM prestador_servicios ps WHERE ps.perfil_prestador_id = '${idPrestador}'
    AND ps.id IN (SELECT prestador_servicio_id FROM disponibilidades) LIMIT 1`)
  const q = new URLSearchParams({
    servicio: srv,
    desde: new Date().toISOString(),
    hasta: new Date(Date.now() + 20 * 86400000).toISOString(),
  })
  const cal = await (await pagina.request.get(`${BASE}/api/prestadores/${SLUG}/bloques?${q}`)).json().catch(() => null)
  if (cal?.bloques) {
    const dentro = cal.bloques.filter((b) => b.inicioEn.slice(0, 10) >= desde && b.inicioEn.slice(0, 10) <= hasta)
    const libresDentro = dentro.filter((b) => b.estado === 'libre')
    comprobar('las horas de los dias bloqueados ya no estan libres',
      dentro.length === 0 || libresDentro.length === 0,
      `en el rango: ${dentro.length} bloques, ${libresDentro.length} libres`)
  } else {
    comprobar('se pudo leer el calendario publico para comprobar el bloqueo', false, 'la ruta del calendario no respondio como se esperaba')
  }

  // --- Quitar el bloqueo --------------------------------------------------------------------
  // Por el nombre EXACTO: las franjas de horario tienen botones «Quitar franja N» en la misma
  // pantalla, y un /quitar/i agarraria el primero de esos, no tocaria el bloqueo y daria por
  // buena una prueba que no probo nada. El del bloqueo se llama «Quitar bloqueo N».
  const quitar = pagina.getByRole('button', { name: /^Quitar bloqueo \d+$/ })
  const hayQuitar = (await quitar.count()) > 0
  comprobar('la pantalla ofrece quitar un bloqueo ya creado', hayQuitar,
    hayQuitar ? '' : 'no hay ningun boton «Quitar» junto al bloqueo')
  if (hayQuitar) {
    await quitar.first().click()
    await pagina.waitForTimeout(2500)
    const quedan = sql(`SELECT count(*) FROM bloqueos_agenda WHERE perfil_prestador_id = '${idPrestador}'`)
    comprobar('quitar el bloqueo lo borra de la base', Number(quedan) === 0, `bloqueos=${quedan}`)
    comprobar('la pantalla vuelve a decir que no hay fechas bloqueadas',
      await pagina.getByText(/no tienes fechas bloqueadas/i).isVisible().catch(() => false))
    await pagina.screenshot({ path: resolve(carpeta, 'bloqueos-02-quitado.png'), fullPage: true })
  }

  console.log(`     (servicio usado: ${nombreServicio})`)
} catch (e) {
  console.log('MAL  el recorrido se detuvo:', e.message.split('\n').slice(0, 6).join(' | '))
  process.exitCode = 1
} finally {
  console.log('\n--- errores de consola ---')
  for (const e of new Set(errores)) console.log('  ', e.slice(0, 250))
  console.log('--- peticiones fallidas ---')
  for (const f of new Set(fallidas)) console.log('  ', f.slice(0, 300))
  console.log(`--- comprobaciones en rojo: ${fallos} ---`)

  for (const id of servicios.split(',').filter(Boolean)) {
    sql(`DELETE FROM contactos WHERE cita_id IN (SELECT c.id FROM citas c JOIN bloques b ON b.id = c.bloque_horario_id WHERE b.prestador_servicio_id = '${id}')`)
    sql(`DELETE FROM citas WHERE bloque_horario_id IN (SELECT id FROM bloques WHERE prestador_servicio_id = '${id}')`)
    sql(`DELETE FROM bloques WHERE prestador_servicio_id = '${id}'`)
    sql(`DELETE FROM disponibilidades WHERE prestador_servicio_id = '${id}'`)
  }
  sql(`DELETE FROM bloqueos_agenda WHERE perfil_prestador_id = '${idPrestador}'`)
  sql(`DELETE FROM usuario_roles WHERE rol_id = 2 AND usuario_id = (SELECT id FROM usuarios WHERE correo::text = '${CORREO_PRESTADOR}')`)
  sql(`UPDATE usuarios SET hash_contrasena = '${HASH_DEMO}' WHERE correo::text = '${CORREO_PRESTADOR}'`)
  sql(`DELETE FROM usuarios WHERE correo::text = '${correoBuscador}'`)
  console.log('limpieza: horario, bloques, bloqueos, rol prestado y cuenta de apoyo borrados; hash de demostracion restaurado')
  await navegador.close()
}
