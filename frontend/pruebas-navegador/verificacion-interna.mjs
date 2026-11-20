/**
 * JQ-11. La mesa del Verificador en un navegador de verdad, contra la API y PostgreSQL locales:
 * cola → tomar → abrir un documento → verificar, y comprobar EN LA BASE que la credencial quedó
 * verificada. También mira la puerta: sin el rol, /interno es una página que no existe.
 *
 * Dos cuentas de demostración: una prestadora que envía una credencial a revisión, y otra a la que
 * este guion le pone el rol `verificador` por base (RN-S2-09: nadie revisa lo propio). Al terminar
 * le quita el rol. La credencial creada queda (con nombre único por corrida), porque ya tiene
 * historial de verificación; así se puede correr dos veces.
 *
 * El paso del documento necesita JQ-9 (API-74 y la subida). Sin JQ-9 en la rama, la credencial se
 * envía sin documentos (`PermitirSinDocumentos` vale true en Development), el paso del documento
 * se informa como OMITIDO y se verifica con el detalle de 20 caracteres.
 *
 * Necesita arriba la API y Vite (`ver-zass.bat`) y el sembrado de demo. La API limita a 5
 * intentos de autenticación cada 15 minutos por IP: para correrlo seguido, `Zass__Tasa__A=100`.
 *
 *   node pruebas-navegador/verificacion-interna.mjs          (desde frontend/)
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
const PRESTADORA = 'demo-camila-painemal@zass.local'
const VERIFICADORA = 'demo-andrea-quilodran@zass.local'
const NOMBRE_CREDENCIAL = `Curso de prueba JQ-11 ${Date.now()}`
const ROL_VERIFICADOR = 3   // roles.id de 'verificador' (migración RolesDeCatalogo)

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

const idDe = (correo) => sql(`SELECT id FROM usuarios WHERE correo::text = '${correo}'`)
const verificadora = idDe(VERIFICADORA)
if (verificadora === '' || idDe(PRESTADORA) === '') {
  console.error('MAL  faltan las cuentas de demostración. Corre el sembrado de demo.')
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

/** Una llamada a la API desde la página, con su cookie y su token CSRF. */
const api = (pagina, ruta, cuerpo) => pagina.evaluate(async ({ ruta, cuerpo }) => {
  const { token } = await (await fetch('/api/auth/csrf', { credentials: 'include' })).json()
  const r = await fetch(ruta, {
    method: 'POST', credentials: 'include',
    headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': token },
    body: cuerpo === undefined ? undefined : JSON.stringify(cuerpo),
  })
  return { estado: r.status, texto: await r.text() }
}, { ruta, cuerpo })

let credencialId = ''
try {
  // 0. La puerta, sin el rol: la misma página que una dirección inventada.
  sql(`DELETE FROM usuario_roles WHERE usuario_id = '${verificadora}' AND rol_id = ${ROL_VERIFICADOR}`)
  const sinRol = await entrar(VERIFICADORA)
  await sinRol.goto(`${BASE}/interno/verificacion`, { waitUntil: 'networkidle' })
  comprobar('sin el rol, /interno/verificacion dice que la página no existe',
    await sinRol.getByRole('heading', { level: 1, name: 'Esta página no existe.' }).isVisible())
  await sinRol.getByRole('button', { name: 'Más' }).click()
  comprobar('sin el rol, «Más» no tiene «Panel interno»',
    (await sinRol.getByRole('link', { name: /Panel interno/ }).count()) === 0)
  await sinRol.context().close()

  // 1. La prestadora crea una credencial y la envía a revisión.
  const prestadora = await entrar(PRESTADORA)
  const creada = await api(prestadora, '/api/mi/credenciales', {
    tipo: 'Curso', nombre: NOMBRE_CREDENCIAL, entidadNombre: 'Instituto de prueba', entidadId: null,
    obtenidaEn: '2025-06-01', venceEn: null,
  })
  comprobar(`la prestadora crea la credencial (${creada.estado})`, creada.estado === 201 || creada.estado === 200)
  credencialId = sql(`SELECT id FROM credenciales WHERE nombre = '${NOMBRE_CREDENCIAL}'`)
  // Con JQ-9 aquí se sube un documento (API-26). Sin JQ-9, la ruta no existe y se sigue sin él.
  const pdf = '%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n'
  const subido = await prestadora.evaluate(async ({ id, pdf }) => {
    const { token } = await (await fetch('/api/auth/csrf', { credentials: 'include' })).json()
    const datos = new FormData()
    datos.append('archivo', new File([pdf], 'certificado.pdf', { type: 'application/pdf' }))
    const r = await fetch(`/api/mi/credenciales/${id}/documentos`, {
      method: 'POST', credentials: 'include', headers: { 'X-CSRF-Token': token }, body: datos,
    })
    return r.status
  }, { id: credencialId, pdf })
  const conDocumento = subido === 201
  console.log(conDocumento ? 'OK   documento subido (JQ-9 presente)' : `INFO sin JQ-9: la subida respondió ${subido}; se sigue sin documento`)
  const enviada = await api(prestadora, `/api/mi/credenciales/${credencialId}/enviar`)
  comprobar(`la credencial pasa a revisión (${enviada.estado})`, enviada.estado < 300)
  await prestadora.context().close()

  // 2. Con el rol puesto por base, la verificadora ve el enlace y la cola.
  sql(`INSERT INTO usuario_roles (usuario_id, rol_id, asignado_en) VALUES ('${verificadora}', ${ROL_VERIFICADOR}, now())
       ON CONFLICT DO NOTHING`)
  const mesa = await entrar(VERIFICADORA)
  await mesa.getByRole('button', { name: 'Más' }).click()
  await mesa.getByRole('link', { name: /Panel interno/ }).click()
  await mesa.getByRole('link', { name: /Verificación de credenciales/ }).click()
  await mesa.waitForURL('**/interno/verificacion')
  const fila = mesa.getByRole('listitem').filter({ hasText: NOMBRE_CREDENCIAL })
  await fila.waitFor()
  comprobar('la credencial está en la cola', await fila.isVisible())
  await mesa.screenshot({ path: resolve(carpeta, 'jq11-cola.png'), fullPage: true })

  // 3. Tomar: la reserva de 30 minutos se ve.
  await fila.getByRole('button', { name: /^Tomar/ }).click()
  const reloj = mesa.getByRole('timer')
  await reloj.waitFor()
  comprobar(`muestra la reserva: «${await reloj.textContent()}»`, /Te quedan (29|30) minutos/.test(await reloj.textContent()))

  // 4. Abrir el documento en una pestaña nueva (solo con JQ-9).
  if (conDocumento) {
    const [pestana] = await Promise.all([
      mesa.context().waitForEvent('page'),
      mesa.getByRole('link', { name: /Abrir certificado\.pdf/ }).click(),
    ])
    const respuesta = await pestana.waitForEvent('response', (r) => r.url().includes('/documentos/'))
    comprobar(`el documento se abre en otra pestaña (${respuesta.status()})`, respuesta.status() === 200)
    await pestana.close()
  } else {
    console.log('OMITIDO abrir el documento: necesita JQ-9 (API-74) en la rama')
  }
  await mesa.screenshot({ path: resolve(carpeta, 'jq11-tomada.png'), fullPage: true })

  // 5. Verificar.
  await mesa.getByRole('radio', { name: 'Verificar' }).check()
  await mesa.getByLabel('Detalle').fill('Comprobado por teléfono con el instituto emisor.')
  await mesa.getByRole('button', { name: 'Guardar resolución' }).click()
  await mesa.getByRole('status').filter({ hasText: 'quedó verificada' }).waitFor()
  comprobar('la pantalla dice que quedó verificada', true)
  comprobar('sale de la cola', (await mesa.getByRole('listitem').filter({ hasText: NOMBRE_CREDENCIAL }).count()) === 0)
  comprobar('en la base quedó Verificada',
    sql(`SELECT estado FROM credenciales WHERE id = '${credencialId}'`) === 'Verificada')
  await mesa.screenshot({ path: resolve(carpeta, 'jq11-resuelta.png'), fullPage: true })
  await mesa.context().close()
} finally {
  sql(`DELETE FROM usuario_roles WHERE usuario_id = '${verificadora}' AND rol_id = ${ROL_VERIFICADOR}`)
  if (credencialId) {
    console.log('INFO la credencial de prueba queda en la base (tiene historial de verificación); nombre:', NOMBRE_CREDENCIAL)
  }
  await navegador.close()
  comprobar('sin errores de JavaScript en la página', errores.length === 0)
  for (const e of errores) console.log('     ', e)
}
