/**
 * Recorre el registro y el ingreso completos contra la API y PostgreSQL de
 * verdad. Esto es lo que ninguna prueba de Vitest puede decir: ahi el fetch
 * esta simulado, aqui viajan cookies reales por el proxy de Vite.
 *
 *   node pruebas-navegador/flujo-real.mjs
 */
import { chromium } from 'playwright'

const correo = `flujo.${Date.now()}@zass.cl`
const clave = 'una-clave-bien-larga'

const navegador = await chromium.launch()
const pagina = await navegador.newPage({ viewport: { width: 420, height: 900 } })
const errores = []
pagina.on('pageerror', (e) => errores.push(e.message))

function comprobar(nombre, bien) {
  console.log(`${bien ? 'OK  ' : 'MAL '} ${nombre}`)
  if (!bien) process.exitCode = 1
}

await pagina.goto('http://localhost:5173/', { waitUntil: 'networkidle' })

// --- Registro ---
await pagina.getByRole('button', { name: /crear una/i }).click()
await pagina.getByLabel(/nombre/i).fill('Pablo del Flujo')
await pagina.getByLabel(/correo/i).fill(correo)
await pagina.getByLabel(/contrase/i).fill(clave)
await pagina.getByRole('radio', { name: /ofrezco/i }).check()
await pagina.getByRole('button', { name: /crear cuenta/i }).click()

await pagina.getByRole('heading', { name: /revisa tu correo/i }).waitFor({ timeout: 15000 })
comprobar('la cuenta se creo y pide verificar el correo', true)
comprobar('muestra el correo al que escribio',
  (await pagina.locator('.tarjeta').textContent()).includes(correo))

// --- Ingreso ---
await pagina.reload({ waitUntil: 'networkidle' })
await pagina.getByLabel(/correo/i).fill(correo)
await pagina.getByLabel(/contrase/i).fill(clave)
await pagina.getByRole('button', { name: /^entrar$/i }).click()

await pagina.getByRole('heading', { name: /ya estas dentro|ya estás dentro/i })
  .waitFor({ timeout: 15000 })
comprobar('entro con la cuenta recien creada', true)

const dentro = await pagina.locator('.tarjeta').textContent()
comprobar('reconoce el rol prestador', dentro.includes('prestador'))
comprobar('avisa que el correo esta sin verificar', /sin verificar/i.test(dentro))

// La cookie de sesion no debe ser legible desde JavaScript.
const visibles = await pagina.evaluate(() => document.cookie)
comprobar('la cookie de sesion es httpOnly (invisible a JS)',
  !visibles.includes('zass_sesion'))

// --- Credenciales malas ---
await pagina.getByRole('button', { name: /salir/i }).click()
await pagina.getByLabel(/correo/i).fill(correo)
await pagina.getByLabel(/contrase/i).fill('esta-no-es-la-clave')
await pagina.getByRole('button', { name: /^entrar$/i }).click()
const alerta = await pagina.getByRole('alert').textContent({ timeout: 15000 })
comprobar('rechaza la clave mala con un mensaje visible', alerta.trim().length > 0)
console.log(`     mensaje: "${alerta.trim()}"`)

comprobar('sin errores de JavaScript en toda la vuelta', errores.length === 0)
if (errores.length) console.log(errores)

await navegador.close()
