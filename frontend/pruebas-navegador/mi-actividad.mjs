/**
 * JQ-8. «Mi actividad» (MI-METRICAS) en un navegador de verdad, contra la API y PostgreSQL locales.
 * Una prestadora de demostración abre «Más» → «Mi actividad» y anota sus contactos por WhatsApp.
 * Otra cuenta (una que todavía no la haya contactado: API-46 cuenta un contacto abierto por
 * persona y prestador) abre su ficha y toca «Escribir por WhatsApp» (API-46 deja la constancia; wa.me se
 * bloquea en este navegador para no salir a internet). La prestadora recarga y el contador subió 1.
 *
 * El contacto queda en la base: es lo que cuenta la pantalla.
 *
 *   node pruebas-navegador/mi-actividad.mjs [carpeta-de-capturas]     (desde frontend/)
 */
import { execFileSync } from 'node:child_process'
import { mkdirSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { chromium } from 'playwright'

const BASE = 'https://localhost:5173'
const raiz = resolve(import.meta.dirname, '..', '..')
const carpeta = process.argv[2] ?? resolve(raiz, 'registros', 'capturas')
mkdirSync(carpeta, { recursive: true })

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

// Una prestadora de demostración con ficha publicada y WhatsApp, que no sea la visitante.
const [correo, slug] = sql(`
  SELECT u.correo || '|' || p.slug FROM perfiles_prestador p JOIN usuarios u ON u.id = p.usuario_id
   WHERE p.estado = 'Publicado' AND coalesce(p.whatsapp, '') <> '' AND u.estado = 'Activa'
     AND u.correo::text LIKE 'demo-%@zass.local'
   ORDER BY u.correo LIMIT 1`).split('|')
if (!correo) {
  console.error('MAL  no hay una prestadora de demostración con WhatsApp. Corre el sembrado de demo.')
  process.exit(1)
}
// Quien toca WhatsApp: una cuenta que todavía no tenga contacto con ella. API-46 deja UN contacto
// abierto por persona y prestador; si la misma persona vuelve a tocar, no suma (es a propósito).
const VISITANTE = sql(`
  SELECT u.correo FROM usuarios u
   WHERE u.correo::text LIKE 'demo-%@zass.local' AND u.estado = 'Activa' AND u.correo_verificado
     AND u.correo::text <> '${correo}'
     AND NOT EXISTS (SELECT 1 FROM contactos c JOIN perfiles_prestador p ON p.id = c.perfil_prestador_id
                      WHERE p.slug = '${slug}' AND c.buscador_id = u.id)
   ORDER BY u.correo LIMIT 1`)
if (!VISITANTE) {
  console.error('MAL  todas las cuentas de demostración ya contactaron a esa prestadora.')
  process.exit(1)
}
console.log(`INFO prestadora ${correo}, ficha /prestadores/${slug}; toca WhatsApp ${VISITANTE}`)

const navegador = await chromium.launch()
const errores = []

async function entrar(cuenta) {
  const contexto = await navegador.newContext({ viewport: { width: 1100, height: 900 }, ignoreHTTPSErrors: true })
  const pagina = await contexto.newPage()
  pagina.on('pageerror', (e) => errores.push(e.message))
  await contexto.route(/wa\.me/, (r) => r.abort())
  await pagina.goto(`${BASE}/entrar`, { waitUntil: 'networkidle' })
  await pagina.getByLabel('Correo').fill(cuenta)
  await pagina.getByLabel('Contraseña').fill(CLAVE_DEMO)
  await pagina.getByRole('button', { name: /^entrar$/i }).click()
  await pagina.waitForURL((u) => !u.pathname.startsWith('/entrar'))
  return pagina
}

/** El valor de una cifra dentro de una sección, por la etiqueta de su <dt>. */
async function cifra(pagina, seccion, etiqueta) {
  const s = pagina.getByRole('region', { name: seccion })
  const caja = s.locator('.cifra').filter({ has: pagina.locator('dt', { hasText: new RegExp(`^${etiqueta}$`) }) }).first()
  return Number((await caja.locator('dd').textContent()).trim())
}

try {
  const prestadora = await entrar(correo)
  await prestadora.getByRole('button', { name: 'Más' }).click()
  await prestadora.waitForTimeout(800)
  await prestadora.screenshot({ path: resolve(carpeta, 'jq8-menu-mas.png') })
  await prestadora.getByRole('link', { name: /Mi actividad/ }).click()
  await prestadora.waitForURL('**/mi-actividad')
  await prestadora.getByRole('region', { name: 'Contactos recibidos' }).waitFor()
  await prestadora.waitForTimeout(800)
  await prestadora.screenshot({ path: resolve(carpeta, 'jq8-mi-actividad-antes.png'), fullPage: true })
  const antes = await cifra(prestadora, 'Contactos recibidos', 'WhatsApp')
  const antes7 = await cifra(prestadora, 'Contactos recibidos', 'Últimos 7 días')
  console.log(`INFO antes: WhatsApp ${antes}, últimos 7 días ${antes7}`)

  // Otra cuenta toca WhatsApp en la ficha.
  const visitante = await entrar(VISITANTE)
  await visitante.goto(`${BASE}/prestadores/${slug}`, { waitUntil: 'networkidle' })
  const contacto = visitante.waitForResponse((r) => r.url().endsWith('/api/contactos') && r.request().method() === 'POST')
  await visitante.getByRole('link', { name: /Escribir por WhatsApp/ }).click()
  const r = await contacto
  comprobar(`tocar WhatsApp registra el contacto (${r.status()})`, r.status() < 300)
  await visitante.screenshot({ path: resolve(carpeta, 'jq8-ficha-visitante.png') })
  await visitante.context().close()

  await prestadora.reload({ waitUntil: 'networkidle' })
  await prestadora.getByRole('region', { name: 'Contactos recibidos' }).waitFor()
  const despues = await cifra(prestadora, 'Contactos recibidos', 'WhatsApp')
  const despues7 = await cifra(prestadora, 'Contactos recibidos', 'Últimos 7 días')
  comprobar(`el contador de WhatsApp sube 1 (${antes} → ${despues})`, despues === antes + 1)
  comprobar(`«Últimos 7 días» sube 1 (${antes7} → ${despues7})`, despues7 === antes7 + 1)
  await prestadora.waitForTimeout(800)
  await prestadora.screenshot({ path: resolve(carpeta, 'jq8-mi-actividad-despues.png'), fullPage: true })

  // En el celular.
  await prestadora.setViewportSize({ width: 390, height: 844 })
  await prestadora.waitForTimeout(500)
  await prestadora.screenshot({ path: resolve(carpeta, 'jq8-mi-actividad-celular.png'), fullPage: true })
  await prestadora.context().close()
} finally {
  await navegador.close()
  comprobar('sin errores de JavaScript en la página', errores.length === 0)
  for (const e of errores) console.log('     ', e)
}
