/**
 * El recorrido completo de un profesional, en un navegador real y contra la API y PostgreSQL de
 * verdad: crea su cuenta, entra, arma su ficha editandola en su lugar, la publica, aparece en la
 * busqueda, su ficha publica la muestra, y sale. Es lo que ninguna prueba de Vitest puede decir:
 * ahi el fetch esta simulado, aqui viajan cookies reales por el proxy de Vite.
 *
 * Necesita arriba la API y Vite. La API limita a 5 intentos de autenticacion cada 15 minutos
 * por IP; para correrlo varias veces seguidas, levantarla con Zass__Tasa__A=100.
 *
 *   node pruebas-navegador/flujo-real.mjs          (desde frontend/)
 */
import { execFileSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { chromium } from 'playwright'

const BASE = 'https://localhost:5173'
const raiz = resolve(import.meta.dirname, '..', '..')
const correo = `flujo.${Date.now()}@zass.local`
const clave = 'una-clave-bien-larga'
const nombre = 'Elena del Flujo'

function sql(consulta) {
  return execFileSync(
    resolve(raiz, 'herramientas/pgsql/bin/psql.exe'),
    ['-h', '127.0.0.1', '-p', '5433', '-U', 'zass', '-d', 'zass', '-At', '-c', consulta],
    { env: { ...process.env, PGPASSWORD: readFileSync(resolve(raiz, '.clave-pg.txt'), 'utf8').trim() } },
  ).toString().trim()
}

const navegador = await chromium.launch()
const pagina = await navegador.newPage({ viewport: { width: 1100, height: 900 }, ignoreHTTPSErrors: true })
const errores = []
pagina.on('pageerror', (e) => errores.push(e.message))
pagina.on('console', (m) => {
  if (m.type() === 'error' && !m.text().includes('404')) errores.push(m.text())
})

function comprobar(texto, bien) {
  console.log(`${bien ? 'OK  ' : 'MAL '} ${texto}`)
  if (!bien) process.exitCode = 1
}
const seccion = (titulo) =>
  pagina.locator('section.bloque', { has: pagina.getByRole('heading', { name: titulo, exact: true }) })
const barra = () => pagina.getByRole('navigation', { name: 'Principal' }).first()

try {
  // --- Crear cuenta de profesional (desde "Para profesionales") ---
  await pagina.goto(`${BASE}/profesionales`, { waitUntil: 'networkidle' })
  await pagina.getByRole('link', { name: /crear mi ficha/i }).first().click()
  await pagina.waitForURL(/crear-cuenta/)
  // Ya no hay dos casillas «busco / ofrezco» que elegir: la cuenta es UNA, y venir desde «Para
  // profesionales» solo trae `?rol=prestador` en la direccion para activarle la ficha de entrada.
  // Este guion se quedo parado meses esperando un control que se quito con esa decision.
  comprobar('"Crear mi ficha" abre la cuenta ya como profesional',
    new URL(pagina.url()).searchParams.get('rol') === 'prestador')
  await pagina.getByLabel('Nombre').fill(nombre)
  await pagina.getByLabel('Correo').fill(correo)
  await pagina.getByLabel('Contraseña').fill(clave)
  await pagina.getByRole('button', { name: /crear cuenta/i }).click()
  await pagina.getByRole('heading', { name: /revisa tu correo/i }).waitFor()
  comprobar('registrarse pide revisar el correo', true)
  sql(`UPDATE usuarios SET correo_verificado = true WHERE correo::text = '${correo}'`)

  // --- Entrar: lleva derecho a Mi ficha ---
  await pagina.goto(`${BASE}/entrar`, { waitUntil: 'networkidle' })
  await pagina.getByLabel('Correo').fill(correo)
  await pagina.getByLabel('Contraseña').fill(clave)
  await pagina.getByRole('button', { name: /^entrar$/i }).click()
  await pagina.waitForURL(/mi-ficha/)
  await pagina.getByRole('heading', { level: 1 }).waitFor()
  comprobar('entrar lleva a Mi ficha', true)
  comprobar('la ficha nueva es un borrador y no se puede publicar aun',
    await pagina.getByText(/Borrador/).first().isVisible()
    && await pagina.getByRole('button', { name: /publicar mi ficha/i }).isDisabled())

  // --- Sobre ti ---
  await seccion('Sobre ti').getByRole('button', { name: /editar|escribir mi presentación/i }).first().click()
  await pagina.getByLabel('Tu nombre, como te van a ver').fill('Elena Flujo Contadora')
  await pagina.getByLabel('Tu oficio').selectOption({ label: 'Contabilidad' })
  await pagina.getByLabel('Sobre ti', { exact: true }).fill('Contadora de pymes en Temuco. Dejo tu contabilidad al día.')
  await pagina.getByLabel('WhatsApp').fill('+56 9 5551 0000')
  await pagina.getByRole('button', { name: 'Guardar', exact: true }).click()
  await pagina.getByRole('heading', { name: 'Elena Flujo Contadora' }).waitFor()
  comprobar('guardar "Sobre ti" actualiza la ficha en su lugar', true)

  // --- Servicios ---
  await seccion('Qué ofreces').getByRole('button', { name: /agregar servicios|editar/i }).first().click()
  await pagina.getByRole('button', { name: /Declaración de renta/ }).click()
  await pagina.getByRole('button', { name: 'Guardar servicios' }).click()
  await pagina.locator('form.editor').waitFor({ state: 'detached' })
  await seccion('Qué ofreces').getByText('Declaración de renta').waitFor()
  comprobar('un servicio sugerido se agrega y se guarda', true)

  // --- Barrios ---
  await seccion('Dónde atiendes').getByRole('button', { name: /elegir mis comunas|editar/i }).first().click()
  // Las 346 comunas se encuentran escribiendo (sin importar tildes ni mayusculas).
  await pagina.getByRole('searchbox', { name: 'Buscar una comuna' }).fill('TEMUCO')
  await pagina.getByRole('checkbox', { name: 'Temuco', exact: true }).check()
  comprobar('la primera comuna elegida queda de principal',
    await pagina.getByRole('radio', { name: /Principal\s*:\s*Temuco/ }).isChecked())
  await pagina.getByRole('button', { name: 'Guardar comunas' }).click()
  await pagina.locator('form.editor').waitFor({ state: 'detached' })
  await seccion('Dónde atiendes').getByText('Temuco').waitFor()
  comprobar('la comuna se guarda como principal', true)

  // --- Publicar ---
  await pagina.getByRole('button', { name: /publicar mi ficha/i }).click()
  await pagina.getByRole('button', { name: /pausar mi ficha/i }).waitFor()
  comprobar('publicar deja la ficha publicada', true)

  // --- Aparece en la busqueda y su ficha publica la muestra ---
  await pagina.goto(`${BASE}/buscar?q=contador&zona=377`, { waitUntil: 'networkidle' })
  const tarjeta = pagina.getByRole('link', { name: /Elena Flujo Contadora/ })
  await tarjeta.waitFor()
  comprobar('aparece en la busqueda de su oficio y su barrio', true)
  await tarjeta.click()
  await pagina.waitForURL(/prestadores\//)
  await pagina.getByRole('heading', { level: 1, name: 'Elena Flujo Contadora' }).waitFor()
  comprobar('su ficha publica muestra el servicio y el contacto',
    await pagina.getByText('Declaración de renta').isVisible()
    && await pagina.getByRole('link', { name: /WhatsApp/ }).isVisible())

  // --- La sesion sobrevive a recargar, y salir la cierra ---
  await pagina.goto(`${BASE}/mi-ficha`, { waitUntil: 'networkidle' })
  comprobar('recargar no pierde la sesion',
    await pagina.getByRole('heading', { level: 1, name: 'Elena Flujo Contadora' }).isVisible())
  await barra().getByRole('button', { name: 'Salir' }).click()
  await barra().getByRole('link', { name: 'Entrar' }).waitFor()
  await pagina.goto(`${BASE}/mi-ficha`, { waitUntil: 'networkidle' })
  comprobar('despues de salir, Mi ficha pide entrar',
    await pagina.getByRole('heading', { level: 1, name: 'Entrar' }).isVisible())

  comprobar('ningun error de consola ni de pagina', errores.length === 0)
  if (errores.length) console.log('     ', [...new Set(errores)].join(' | '))
} catch (e) {
  console.log('MAL  el recorrido se detuvo:', e.message.split('\n').slice(0, 8).join(" | "))
  await pagina.screenshot({ path: resolve(raiz, 'registros/capturas/flujo-error.png'), fullPage: true })
  process.exitCode = 1
} finally {
  // Lo creado se borra: queda la base como estaba, publicado incluido.
  sql(`DELETE FROM usuarios WHERE correo::text = '${correo}'`)
  await navegador.close()
}
