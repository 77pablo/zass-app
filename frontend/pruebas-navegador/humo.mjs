import { chromium } from 'playwright'

const salida = process.argv[2]
const navegador = await chromium.launch()
const pagina = await navegador.newPage({
  viewport: { width: 420, height: 860 },
  // Certificado de desarrollo, sin autoridad que lo firme. Solo para localhost.
  ignoreHTTPSErrors: true,
})

const errores = []
pagina.on('console', (m) => { if (m.type() === 'error') errores.push(m.text()) })
pagina.on('pageerror', (e) => errores.push('pageerror: ' + e.message))

await pagina.goto('https://localhost:5173/', { waitUntil: 'networkidle' })

// La portada es la busqueda. Esta prueba mira la pantalla de ingreso, asi que se va a
// ella por la barra, que es como llega una persona.
await pagina.getByRole('navigation').getByRole('button', { name: /^entrar$/i }).click()

// Lo que probaria una pantalla en blanco: el root existe pero esta vacio.
const dentroDelRoot = await pagina.locator('#root').innerHTML()
console.log('root vacio?           ', dentroDelRoot.trim() === '' ? 'SI - PANTALLA EN BLANCO' : 'no, monto')
console.log('titulo visible        ', await pagina.getByRole('heading', { name: /entrar/i }).isVisible())
console.log('campo correo          ', await pagina.getByLabel(/correo/i).isVisible())
console.log('campo contrasena      ', await pagina.getByLabel(/contrase/i).isVisible())
console.log('fuente aplicada       ', await pagina.evaluate(() => getComputedStyle(document.body).fontFamily.split(',')[0]))
console.log('fondo crema           ', await pagina.evaluate(() => getComputedStyle(document.body).backgroundColor))

// Los colores se miden, no se juzgan por la captura: el PNG engaña.
// OJO: la busqueda sigue montada pero escondida, asi que un querySelector('.boton') a
// secas devolveria SU boton (blanco) y esta comprobacion mediria otra cosa sin avisar.
const color = (localizador, prop) =>
  localizador.evaluate((el, p) => getComputedStyle(el)[p], prop)
console.log('boton, fondo          ',
  await color(pagina.getByRole('button', { name: /^entrar$/i }), 'backgroundColor'),
  '(se espera 246,200,30)')
console.log('texto secundario      ',
  await color(pagina.locator('.bajada:visible'), 'color'), '(se espera 107,107,107)')
console.log('tilde en la etiqueta  ', await pagina.locator('label[for="contrasena"]').textContent())

await pagina.screenshot({ path: salida + '/pantalla-entrar.png' })

// Pasa a crear cuenta y fotografia
await pagina.getByRole('button', { name: /crear una/i }).click()
console.log('cambio a registro     ', await pagina.getByRole('heading', { name: /crear cuenta/i }).isVisible())
await pagina.screenshot({ path: salida + '/pantalla-registro.png' })

console.log('errores de consola    ', errores.length === 0 ? 'ninguno' : errores.join(' | '))
await navegador.close()
