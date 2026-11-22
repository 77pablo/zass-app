/**
 * JQ-13. Emitir tarjetas (API-59) y la auditoría (API-61) en un navegador de verdad, contra la API
 * y PostgreSQL locales. Una cuenta de demostración recibe el rol `administrador` por base.
 *
 * Emitir: el código se ve una vez, la vista de impresión muestra solo la etiqueta, recargar pide
 * confirmación al navegador y después el código ya no está, y nunca pasa por localStorage,
 * sessionStorage ni la URL. Auditoría: la tarjeta recién emitida se encuentra por entidad e id, y
 * «Ver más antiguos» trae la página siguiente de una persona con más de 50 registros.
 *
 * La tarjeta emitida queda en la base (Emitida, sin activar): es historial de auditoría.
 *
 *   node pruebas-navegador/tarjetas-auditoria-interna.mjs [carpeta-de-capturas]     (desde frontend/)
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
const ADMINISTRADORA = 'demo-andrea-quilodran@zass.local'
const ROL_ADMINISTRADOR = 5   // roles.id de 'administrador' (migración RolesDeCatalogo)

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

const administradora = sql(`SELECT id FROM usuarios WHERE correo::text = '${ADMINISTRADORA}'`)
if (administradora === '') {
  console.error('MAL  falta la cuenta de demostración. Corre el sembrado de demo.')
  process.exit(1)
}

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

const pausa = (p) => p.waitForTimeout(800)   // que terminen las transiciones antes de cada captura

try {
  sql(`INSERT INTO usuario_roles (usuario_id, rol_id, asignado_en) VALUES ('${administradora}', ${ROL_ADMINISTRADOR}, now())
       ON CONFLICT DO NOTHING`)
  const p = await entrar(ADMINISTRADORA)
  await p.getByRole('button', { name: 'Más' }).click()
  await p.getByRole('link', { name: /Panel interno/ }).click()
  await p.waitForURL('**/interno')
  await pausa(p)
  await p.screenshot({ path: resolve(carpeta, 'jq13-panel-administrador.png'), fullPage: true })

  // ------------------------------------------------------------------ emitir
  await p.getByRole('link', { name: /Emitir tarjetas/ }).click()
  await p.waitForURL('**/interno/tarjetas')
  await pausa(p)
  await p.screenshot({ path: resolve(carpeta, 'jq13-emitir-antes.png'), fullPage: true })

  const respuesta = p.waitForResponse((r) => r.url().endsWith('/api/interno/tarjetas') && r.request().method() === 'POST')
  await p.getByRole('button', { name: 'Emitir una tarjeta' }).click()
  const emitida = await (await respuesta).json()
  const seccion = p.getByRole('region', { name: 'Código de activación' })
  await seccion.waitFor()
  const codigo = (await p.locator('.codigo-activacion').textContent()).trim()
  comprobar(`se ve el código de activación (${codigo.length} caracteres)`, codigo.length > 0)
  comprobar('se ve el aviso de una sola vez', await p.getByRole('alert').filter({ hasText: 'no lo volveremos a mostrar' }).isVisible())
  // El QR se dibuja un instante después del código: se espera, con un tope, en vez de mirar una vez.
  const qr = p.getByRole('img', { name: /Código QR de/ })
  await qr.waitFor({ timeout: 10000 }).catch(() => {})
  comprobar('se ve el código QR', await qr.isVisible())
  await pausa(p)
  await p.screenshot({ path: resolve(carpeta, 'jq13-emitida.png'), fullPage: true })

  const almacen = await p.evaluate(() => JSON.stringify({ ...localStorage }) + JSON.stringify({ ...sessionStorage }) + location.href)
  comprobar('el código no está en localStorage, sessionStorage ni la URL', !almacen.includes(codigo))

  // Vista de impresión: solo la etiqueta.
  await p.emulateMedia({ media: 'print' })
  // La regla de impresión oculta todo con `visibility` menos la etiqueta: se cuenta el texto que
  // queda visible fuera de ella.
  const sobrantes = await p.evaluate(() => {
    const etiqueta = document.querySelector('.etiqueta-imprimible')
    return [...document.body.querySelectorAll('*')].filter((e) =>
      !etiqueta.contains(e) && getComputedStyle(e).visibility === 'visible'
      && [...e.childNodes].some((n) => n.nodeType === 3 && n.textContent.trim() !== '')).map((e) => e.textContent.trim().slice(0, 40))
  })
  comprobar(`al imprimir solo se ve la etiqueta (${sobrantes.length} textos fuera de ella)`, sobrantes.length === 0)
  for (const t of sobrantes) console.log('      sobra:', t)
  await p.screenshot({ path: resolve(carpeta, 'jq13-vista-de-impresion.png'), fullPage: true })
  // El PDF que saldría de la impresora: cuántas hojas.
  const pdf = await p.pdf({ path: resolve(carpeta, 'jq13-etiqueta.pdf'), format: 'A4' })
  const hojas = (pdf.toString('latin1').match(/\/Type\s*\/Page[^s]/g) ?? []).length
  comprobar(`la etiqueta sale en una sola hoja (${hojas})`, hojas === 1)
  await p.emulateMedia({ media: 'screen' })

  // Recargar: el navegador pide confirmación, y después el código ya no está.
  let advertencia = ''
  p.once('dialog', async (d) => { advertencia = d.type(); await d.accept() })
  await p.reload({ waitUntil: 'networkidle' })
  comprobar(`al recargar, el navegador advierte (diálogo «${advertencia}»)`, advertencia === 'beforeunload')
  comprobar('después de recargar, el código ya no está', !(await p.locator('main').innerText()).includes(codigo))
  await pausa(p)
  await p.screenshot({ path: resolve(carpeta, 'jq13-despues-de-recargar.png'), fullPage: true })

  // ------------------------------------------------------------------ auditoría
  const tarjetaId = emitida.id ?? emitida.tarjetaId ?? sql('SELECT id FROM tarjetas ORDER BY emitida_en DESC LIMIT 1')
  const entidad = sql(`SELECT entidad FROM auditoria WHERE entidad_id = '${tarjetaId}' ORDER BY ocurrido_en LIMIT 1`)
  console.log(`INFO tarjeta ${tarjetaId}; en auditoría la entidad se llama «${entidad}»`)
  await p.goto(`${BASE}/interno`, { waitUntil: 'networkidle' })
  await p.getByRole('link', { name: /Auditoría/ }).click()
  await p.waitForURL('**/interno/auditoria')
  await p.getByLabel('Entidad', { exact: true }).fill('Tarjeta')
  await p.getByLabel('Identificador del registro').fill(tarjetaId)
  await p.getByRole('button', { name: 'Buscar' }).click()
  const filas = p.locator('table tbody tr')
  await filas.first().waitFor({ timeout: 10000 }).catch(() => {})
  const porTarjeta = await filas.count()
  comprobar(`buscar «Tarjeta» + id encuentra la emisión (${porTarjeta} filas)`, porTarjeta > 0)
  await pausa(p)
  await p.screenshot({ path: resolve(carpeta, 'jq13-auditoria-tarjeta.png'), fullPage: true })

  // «Ver más antiguos»: hace falta alguien con más de 50 registros (la página trae 50).
  // Si nadie llega a 50, se pide a la API REAL una página de 3 (se agrega `limite=3` a la petición
  // en este navegador): el cursor y la página siguiente son los de verdad, solo cambia el tamaño.
  let porPagina = 50
  let actor = sql(`SELECT actor_id FROM auditoria WHERE actor_id IS NOT NULL
                   GROUP BY actor_id HAVING count(*) > 50 ORDER BY count(*) DESC LIMIT 1`)
  if (actor === '') {
    porPagina = 3
    actor = sql(`SELECT actor_id FROM auditoria WHERE actor_id IS NOT NULL
                 GROUP BY actor_id HAVING count(*) > 3 ORDER BY count(*) DESC LIMIT 1`)
    await p.route('**/api/interno/auditoria?**', (ruta) => ruta.continue({ url: `${ruta.request().url()}&limite=3` }))
    console.log('INFO nadie tiene más de 50 registros: «Ver más antiguos» se prueba con páginas de 3')
  }
  if (actor === '') {
    console.log('OMITIDO «Ver más antiguos»: en la base nadie tiene más de 3 registros de auditoría')
  } else {
    const total = +sql(`SELECT count(*) FROM auditoria WHERE actor_id = '${actor}'`)
    await p.getByRole('radio', { name: 'Persona que actuó' }).check()
    await p.getByLabel('Identificador de la persona').fill(actor)
    await p.getByRole('button', { name: 'Buscar' }).click()
    await p.getByRole('button', { name: 'Ver más antiguos' }).waitFor()
    const primera = await filas.count()
    await p.getByRole('button', { name: 'Ver más antiguos' }).click()
    await p.waitForFunction((n) => document.querySelectorAll('table tbody tr').length > n, primera)
    const segunda = await filas.count()
    comprobar(`«Ver más antiguos» suma filas: ${primera} → ${segunda} (páginas de ${porPagina}; la persona tiene ${total})`,
      segunda > primera && segunda === Math.min(total, primera + porPagina))
    await pausa(p)
    await p.screenshot({ path: resolve(carpeta, 'jq13-auditoria-mas-antiguos.png'), fullPage: true })
  }
  await p.context().close()
} finally {
  sql(`DELETE FROM usuario_roles WHERE usuario_id = '${administradora}' AND rol_id = ${ROL_ADMINISTRADOR}`)
  console.log('INFO rol de administrador quitado')
  await navegador.close()
  comprobar('sin errores de JavaScript en la página', errores.length === 0)
  for (const e of errores) console.log('     ', e)
}
