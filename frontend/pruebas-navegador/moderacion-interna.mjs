/**
 * JQ-12. La moderación de reseñas en un navegador de verdad, contra la API y PostgreSQL locales:
 * una cuenta de demostración reporta una reseña (API-51), otra con el rol `moderador` (puesto por
 * base) la ve en /interno/reportes, intenta guardar sin motivo, y la retira con motivo. Se comprueba
 * que sale de la lista SIN recargar y que en la base quedó Retirada.
 *
 * Al terminar quita el rol y devuelve la reseña al estado que tenía, para no dejar la demo con una
 * reseña menos. El reporte queda resuelto en la base (es historial).
 *
 *   node pruebas-navegador/moderacion-interna.mjs [carpeta-de-capturas]     (desde frontend/)
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
const MODERADORA = 'demo-andrea-quilodran@zass.local'
const ROL_MODERADOR = 4   // roles.id de 'moderador' (migración RolesDeCatalogo)

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

const moderadora = sql(`SELECT id FROM usuarios WHERE correo::text = '${MODERADORA}'`)
if (moderadora === '') {
  console.error('MAL  falta la cuenta de demostración. Corre el sembrado de demo.')
  process.exit(1)
}

// Una reseña publicada que no sea de la moderadora ni sobre ella, sin reportes abiertos.
const [resenaId, estadoOriginal, autorId, duenoId] = sql(`
  SELECT r.id || '|' || r.estado || '|' || r.autor_id || '|' || p.usuario_id
    FROM resenas r JOIN perfiles_prestador p ON p.id = r.perfil_prestador_id
   WHERE r.estado = 'Publicada' AND r.autor_id <> '${moderadora}' AND p.usuario_id <> '${moderadora}'
     AND NOT EXISTS (SELECT 1 FROM reportes_resena x WHERE x.resena_id = r.id AND x.estado IN ('Abierto', 'EnRevision'))
   ORDER BY r.publicada_en DESC LIMIT 1`).split('|')
// Quien reporta: otra cuenta de demostración, activa y con correo verificado.
const reportante = sql(`
  SELECT correo FROM usuarios
   WHERE correo::text LIKE 'demo-%@zass.local' AND correo_verificado AND estado = 'Activa'
     AND id NOT IN ('${moderadora}', '${autorId}', '${duenoId}')
   ORDER BY correo LIMIT 1`)
if (!resenaId || !reportante) {
  console.error('MAL  no hay una reseña publicada o una cuenta para reportarla en la demo.')
  process.exit(1)
}
console.log(`INFO reseña ${resenaId} (estaba ${estadoOriginal}); la reporta ${reportante}`)

const navegador = await chromium.launch()
const errores = []

async function entrar(correo) {
  const contexto = await navegador.newContext({ viewport: { width: 1100, height: 900 }, ignoreHTTPSErrors: true })
  const pagina = await contexto.newPage()
  pagina.on('pageerror', (e) => errores.push(e.message))
  await pagina.goto(`${BASE}/entrar`, { waitUntil: 'networkidle' })
  await pagina.getByLabel('Correo').fill(correo)
  await pagina.getByLabel('Contraseña').fill(CLAVE_DEMO)
  await pagina.getByRole('button', { name: /^entrar$/i }).click()
  await pagina.waitForURL((u) => !u.pathname.startsWith('/entrar'))
  return pagina
}

try {
  // 1. Otra cuenta reporta la reseña.
  const quienReporta = await entrar(reportante)
  const reporte = await quienReporta.evaluate(async (id) => {
    const { token } = await (await fetch('/api/auth/csrf', { credentials: 'include' })).json()
    const r = await fetch(`/api/resenas/${id}/reporte`, {
      method: 'POST', credentials: 'include',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': token },
      body: JSON.stringify({ motivo: 'FueraDeTema', detalle: 'Reporte de prueba visual JQ-12.' }),
    })
    return { estado: r.status, texto: await r.text() }
  }, resenaId)
  comprobar(`la reseña se reporta (${reporte.estado})`, reporte.estado === 204)
  if (reporte.estado !== 204) console.log('     ', reporte.texto)
  await quienReporta.context().close()

  // 2. Con el rol puesto por base, la moderadora entra por el panel.
  sql(`INSERT INTO usuario_roles (usuario_id, rol_id, asignado_en) VALUES ('${moderadora}', ${ROL_MODERADOR}, now())
       ON CONFLICT DO NOTHING`)
  const mesa = await entrar(MODERADORA)
  await mesa.getByRole('button', { name: 'Más' }).click()
  await mesa.getByRole('link', { name: /Panel interno/ }).click()
  await mesa.waitForURL('**/interno')
  await mesa.waitForTimeout(800)   // que termine la transición de entrada antes de la captura
  await mesa.screenshot({ path: resolve(carpeta, 'jq12-panel.png'), fullPage: true })
  await mesa.getByRole('link', { name: /Reseñas reportadas/ }).click()
  await mesa.waitForURL('**/interno/reportes')
  const lista = mesa.getByRole('list', { name: 'Reseñas reportadas' })
  await lista.waitFor()
  const filas = lista.getByRole('listitem')
  const antes = await filas.count()
  const fila = filas.filter({ hasText: 'Reporte de prueba visual JQ-12.' }).first()
  await fila.waitFor()
  comprobar(`el reporte está en la cola (${antes} en total)`, await fila.isVisible())
  await mesa.screenshot({ path: resolve(carpeta, 'jq12-cola.png'), fullPage: true })

  // 3. Sin motivo no se puede guardar.
  await fila.getByRole('radio', { name: /Retirar/ }).check()
  await fila.getByRole('button', { name: 'Guardar decisión' }).click()
  const aviso = fila.getByRole('alert')
  await aviso.waitFor()
  comprobar(`sin motivo, avisa: «${await aviso.textContent()}»`, /al menos 10/.test(await aviso.textContent()))
  comprobar('sin motivo, la reseña sigue igual en la base',
    sql(`SELECT estado FROM resenas WHERE id = '${resenaId}'`) !== 'Retirada')
  await fila.screenshot({ path: resolve(carpeta, 'jq12-sin-motivo.png') })

  // 4. Con motivo, se retira y sale de la lista sin recargar.
  let recargas = 0
  mesa.on('framenavigated', (f) => { if (f === mesa.mainFrame()) recargas++ })
  await fila.getByLabel('Motivo de la decisión').fill('Prueba visual: la reseña no habla del servicio.')
  await fila.getByRole('button', { name: 'Guardar decisión' }).click()
  await mesa.getByRole('status').filter({ hasText: 'retirada' }).waitFor()
  comprobar('la pantalla dice que quedó retirada', true)
  comprobar('sale de la lista', (await mesa.getByText('Reporte de prueba visual JQ-12.').count()) === 0)
  comprobar('sin recargar la página', recargas === 0)
  comprobar('en la base quedó Retirada', sql(`SELECT estado FROM resenas WHERE id = '${resenaId}'`) === 'Retirada')
  await mesa.screenshot({ path: resolve(carpeta, 'jq12-retirada.png'), fullPage: true })
  await mesa.context().close()
} finally {
  sql(`DELETE FROM usuario_roles WHERE usuario_id = '${moderadora}' AND rol_id = ${ROL_MODERADOR}`)
  sql(`UPDATE resenas SET estado = '${estadoOriginal}' WHERE id = '${resenaId}'`)
  console.log(`INFO rol quitado y reseña devuelta a ${estadoOriginal}`)
  await navegador.close()
  comprobar('sin errores de JavaScript en la página', errores.length === 0)
  for (const e of errores) console.log('     ', e)
}
