/**
 * JQ-14. Los indicadores (API-60) en un navegador de verdad, contra la API y PostgreSQL locales.
 * Una cuenta de demostración recibe el rol `administrador` por base, entra por el panel y abre
 * /interno/indicadores. Cada cifra se compara con la misma cuenta hecha en SQL sobre la base.
 *
 * El caso «sin resoluciones» no se puede forzar en la base de desarrollo sin borrar historial, así
 * que se ve con la respuesta de la API sustituida en el navegador (page.route): solo cambia lo que
 * recibe la pantalla, no la base. La captura lo dice en su nombre.
 *
 *   node pruebas-navegador/indicadores-interna.mjs [carpeta-de-capturas]     (desde frontend/)
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

/** Las mismas cuentas que ConsultaIndicadoresEfCore, escritas en SQL. */
function enLaBase() {
  const [enCola, resueltas, rechazadas, horas, fallidos, reportes, sinActivar] = sql(`
    SELECT (SELECT count(*) FROM credenciales WHERE estado = 'EnRevision')
      || '|' || count(*)
      || '|' || count(*) FILTER (WHERE v.resultado = 'Rechazada')
      || '|' || coalesce(round(avg(extract(epoch FROM v.resuelta_en - c.creada_en) / 3600)::numeric, 2)::text, '')
      || '|' || (SELECT count(*) FROM notificaciones WHERE canal = 'Correo' AND estado = 'Fallida'
                   AND programada_para >= now() - interval '7 days' AND programada_para <= now())
      || '|' || (SELECT count(*) FROM reportes_resena WHERE estado IN ('Abierto', 'EnRevision'))
      || '|' || (SELECT count(*) FROM tarjetas WHERE estado = 'Emitida')
      FROM verificaciones v JOIN credenciales c ON c.id = v.credencial_id
     WHERE v.resuelta_en >= now() - interval '30 days' AND v.resuelta_en <= now()`).split('|')
  return {
    enCola: +enCola, resueltas: +resueltas, rechazadas: +rechazadas,
    horas: horas === '' ? null : +horas, fallidos: +fallidos, reportes: +reportes, sinActivar: +sinActivar,
  }
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

try {
  sql(`INSERT INTO usuario_roles (usuario_id, rol_id, asignado_en) VALUES ('${administradora}', ${ROL_ADMINISTRADOR}, now())
       ON CONFLICT DO NOTHING`)
  const pagina = await entrar(ADMINISTRADORA)
  await pagina.getByRole('button', { name: 'Más' }).click()
  await pagina.getByRole('link', { name: /Panel interno/ }).click()
  await pagina.waitForURL('**/interno')
  await pagina.waitForTimeout(800)   // que termine la transición de entrada antes de la captura
  await pagina.screenshot({ path: resolve(carpeta, 'jq14-panel-administrador.png'), fullPage: true })

  const respuesta = pagina.waitForResponse((r) => r.url().endsWith('/api/interno/indicadores'))
  await pagina.getByRole('link', { name: /Indicadores/ }).click()
  await pagina.waitForURL('**/interno/indicadores')
  const api = await (await respuesta).json()
  await pagina.getByText('Calculado el').waitFor()
  await pagina.waitForTimeout(800)
  await pagina.screenshot({ path: resolve(carpeta, 'jq14-indicadores.png'), fullPage: true })

  const b = enLaBase()
  const v = api.verificacion
  console.log('INFO API :', JSON.stringify({ ...v, fallidos: api.avisos.correosFallidos, reportes: api.reportesAbiertos, sinActivar: api.tarjetasEmitidasSinActivar }))
  console.log('INFO base:', JSON.stringify(b))
  comprobar(`credenciales en cola: API ${v.enCola}, base ${b.enCola}`, v.enCola === b.enCola)
  comprobar(`resueltas en 30 días: API ${v.resueltas}, base ${b.resueltas}`, v.resueltas === b.resueltas)
  comprobar(`rechazadas en 30 días: API ${v.rechazadas}, base ${b.rechazadas}`, v.rechazadas === b.rechazadas)
  comprobar(`horas promedio: API ${v.horasPromedioDesdeCreacion?.toFixed(2)}, base ${b.horas}`,
    b.horas === null ? v.horasPromedioDesdeCreacion === null : Math.abs(v.horasPromedioDesdeCreacion - b.horas) < 0.05)
  comprobar(`correos fallidos en 7 días: API ${api.avisos.correosFallidos}, base ${b.fallidos}`, api.avisos.correosFallidos === b.fallidos)
  comprobar(`reportes sin resolver: API ${api.reportesAbiertos}, base ${b.reportes}`, api.reportesAbiertos === b.reportes)
  comprobar(`tarjetas sin activar: API ${api.tarjetasEmitidasSinActivar}, base ${b.sinActivar}`, api.tarjetasEmitidasSinActivar === b.sinActivar)

  // La pantalla muestra lo que dijo la API.
  const texto = await pagina.locator('main').innerText()
  const tasa = v.tasaDeRechazo === null ? 'Sin datos todavía' : `${Math.round(v.tasaDeRechazo * 100)} %`
  comprobar(`la pantalla muestra la tasa «${tasa}»`, texto.includes(tasa))
  comprobar(`la pantalla muestra «${v.rechazadas} de ${v.resueltas} resueltas»`,
    v.resueltas === 0 || texto.includes(`${v.rechazadas} de ${v.resueltas} resueltas`))

  // Sin resoluciones en la ventana: la respuesta sustituida, solo en este navegador.
  await pagina.route('**/api/interno/indicadores', (ruta) => ruta.fulfill({
    status: 200, contentType: 'application/json',
    body: JSON.stringify({
      ...api,
      verificacion: { ...v, resueltas: 0, rechazadas: 0, tasaDeRechazo: null, horasPromedioDesdeCreacion: null },
    }),
  }))
  await pagina.reload({ waitUntil: 'networkidle' })
  await pagina.getByText('Calculado el').waitFor()
  const vacio = await pagina.locator('main').innerText()
  comprobar('con 0 resoluciones dice «Sin datos todavía»', vacio.includes('Sin datos todavía'))
  comprobar('con 0 resoluciones no dice «0 %»', !vacio.includes('0 %'))
  await pagina.waitForTimeout(800)
  await pagina.screenshot({ path: resolve(carpeta, 'jq14-sin-resoluciones-SIMULADO.png'), fullPage: true })
  await pagina.context().close()
} finally {
  sql(`DELETE FROM usuario_roles WHERE usuario_id = '${administradora}' AND rol_id = ${ROL_ADMINISTRADOR}`)
  console.log('INFO rol de administrador quitado')
  await navegador.close()
  comprobar('sin errores de JavaScript en la página', errores.length === 0)
  for (const e of errores) console.log('     ', e)
}
