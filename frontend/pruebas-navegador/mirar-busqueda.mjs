/**
 * Mira la pantalla de busqueda en un navegador real y la MIDE, no solo la fotografia.
 *
 * Vitest corre en jsdom: ahi no hay CSS, ni layout, ni fuentes. Este guion es lo unico
 * que puede decir si algo se desborda, si un boton es demasiado chico para un dedo o si
 * un color es el de la marca.
 *
 * Necesita la base, la API y la interfaz arriba (ver-zass.bat), y prestadores de prueba:
 *
 *   herramientas\pgsql\bin\psql.exe -h 127.0.0.1 -p 5433 -U zass -d zass ^
 *       -f frontend\pruebas-navegador\datos\sembrar-visual.sql
 *   node pruebas-navegador/mirar-busqueda.mjs         (desde frontend/)
 *   herramientas\pgsql\bin\psql.exe ... -f frontend\pruebas-navegador\datos\limpiar-visual.sql
 *
 * *** NO TE OLVIDES DEL ULTIMO PASO. *** Los prestadores sembrados quedan publicados con
 * suscripcion vigente, y las pruebas de integracion de la busqueda esperan una base sin
 * prestadores visibles: con ellos ahi, fallan. Las capturas quedan en registros/capturas/.
 */
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
import { chromium } from 'playwright'

const carpeta = resolve(import.meta.dirname, '..', '..', 'registros', 'capturas')
mkdirSync(carpeta, { recursive: true })

const navegador = await chromium.launch()
const errores = []
let fallos = 0

function comprobar(nombre, bien, detalle = '') {
  console.log(`${bien ? 'OK  ' : 'MAL '} ${nombre}${detalle ? `  (${detalle})` : ''}`)
  if (!bien) fallos++
}

async function nuevaPagina(ancho, alto, extras = {}) {
  const contexto = await navegador.newContext({
    viewport: { width: ancho, height: alto },
    deviceScaleFactor: 2,
    ignoreHTTPSErrors: true,
    ...extras,
  })
  const pagina = await contexto.newPage()
  pagina.on('pageerror', (e) => errores.push(`[${ancho}] ${e.message}`))
  pagina.on('console', (m) => {
    if (m.type() === 'error') errores.push(`[${ancho}] consola: ${m.text()}`)
  })
  return pagina
}

async function buscarCorretajeEnElCentro(pagina) {
  await pagina.getByLabel(/qué necesitas/i).selectOption('corretaje')
  await pagina.getByLabel(/en qué zona/i).selectOption({ label: 'Centro' })
  await pagina.getByRole('button', { name: /^busca/i }).click()
  await pagina.locator('.resultado').first().waitFor({ timeout: 15000 })
}

// ------------------------------------------------------------------ movil
const movil = await nuevaPagina(390, 844)
await movil.goto('https://localhost:5173/', { waitUntil: 'networkidle' })
await movil.getByRole('option', { name: 'Corretaje de propiedades' }).waitFor({ state: 'attached' })
await movil.screenshot({ path: resolve(carpeta, '01-portada-movil.png'), fullPage: true })

comprobar('la portada es la busqueda',
  await movil.getByRole('heading', { name: /^buscar$/i }).isVisible())
comprobar('hay un enlace para entrar',
  await movil.getByRole('navigation').getByRole('button', { name: /^entrar$/i }).isVisible())

await buscarCorretajeEnElCentro(movil)
await movil.screenshot({ path: resolve(carpeta, '02-resultados-movil.png'), fullPage: true })

// --- lo que debe y no debe salir (RN-S3-01, RN-S3-09 y el filtro de rubro)
const nombres = await movil.locator('.resultado-nombre').allTextContents()
console.log('     resultados:', nombres.join(' | '))
comprobar('salen los 5 prestadores buscables', nombres.length === 5, `salieron ${nombres.length}`)

// EL ORDEN. Este guion no lo miraba, y por eso un 5,0 de 2 resenas (que la pantalla no
// muestra, RN-S5-11) paso DELANTE de un 4,6 de 12 sin que ninguna comprobacion se
// quejara: solo se vio mirando la captura. Verificados primero, luego reputacion
// publica, luego mas resenas.
const ordenPorZona = [
  'Corretaje Araucanía', 'Gestión Inmobiliaria Labranza',
  'Estudio Nuevo Temuco', 'Asesoría Integral', 'Propiedades del Sur',
]
comprobar('por zona: verificados primero y despues por reseñas, sin que un promedio oculto adelante a nadie',
  ordenPorZona.every((inicio, i) => nombres[i]?.startsWith(inicio)),
  nombres.map((n) => n.slice(0, 18)).join(' > '))
comprobar('no sale el de suscripcion vencida', !nombres.some((n) => /vencido/i.test(n)))
comprobar('no sale el perfil en borrador', !nombres.some((n) => /borrador/i.test(n)))
comprobar('no sale el de otro rubro', !nombres.some((n) => /abogados/i.test(n)))
comprobar('el que cubre Centro como zona secundaria si sale',
  nombres.some((n) => /labranza/i.test(n)))

// --- RN-S3-02 sobre lo que de verdad llega al navegador
const html = await movil.content()
comprobar('el HTML no lleva coordenadas ni correos',
  !/@zass\.local|latitud|longitud/i.test(html))

// --- sello, resenas y distancia
const tarjeta = movil.locator('.resultado', { hasText: 'Corretaje Araucanía' })
comprobar('Corretaje Araucania lleva sello', await tarjeta.locator('.sello').isVisible())
comprobar('y su promedio con coma', (await tarjeta.textContent()).includes('4,6 de 5'))
const nuevo = movil.locator('.resultado', { hasText: 'Estudio Nuevo Temuco' })
comprobar('2 resenas: el recuento, no el 5,0',
  (await nuevo.textContent()).includes('2 reseñas') && !(await nuevo.textContent()).includes('5,0'))
comprobar('sin ordenar por distancia, no hay distancia y la pantalla lo avisa',
  (await movil.locator('.aviso--info').textContent()).includes('distancia'))

// --- desborde horizontal: en un celular es lo primero que se rompe
const medidas = await movil.evaluate(() => ({
  pagina: document.documentElement.scrollWidth,
  ventana: document.documentElement.clientWidth,
}))
comprobar('no hay desborde horizontal a 390 px', medidas.pagina <= medidas.ventana,
  `contenido ${medidas.pagina} / ventana ${medidas.ventana}`)

// --- el nombre larguisimo tiene que partirse y no salirse de su tarjeta
const larga = movil.locator('.resultado', { hasText: 'Asesoría Integral' })
const caja = await larga.boundingBox()
const nombreLargo = await larga.locator('.resultado-nombre').boundingBox()
comprobar('el nombre largo no se sale de su tarjeta',
  nombreLargo.x + nombreLargo.width <= caja.x + caja.width + 0.5)

// --- objetivos tactiles: 44 px es el minimo recomendado para un dedo
const chicos = await movil.evaluate(() => {
  // Un enlace DENTRO de una frase (el credito de OpenStreetMap) esta exento: WCAG 2.5.8
  // exceptua los enlaces en linea. Un enlace que es un objetivo aparte, no.
  const candidatos = [
    ...document.querySelectorAll('button, select, .casilla, a[href]'),
  ].filter((el) => el.offsetParent !== null && !el.closest('.pie'))
  return candidatos
    .map((el) => {
      const c = el.getBoundingClientRect()
      return { texto: (el.textContent || el.tagName).trim().slice(0, 28), alto: Math.round(c.height) }
    })
    .filter((x) => x.alto < 44)
})
comprobar('todos los botones, listas y casillas miden al menos 44 px de alto',
  chicos.length === 0, chicos.map((x) => `${x.texto}=${x.alto}px`).join(', '))

// --- colores de la marca, medidos y no supuestos
const colores = await movil.evaluate(() => {
  const css = (sel, prop) => getComputedStyle(document.querySelector(sel))[prop]
  return {
    botonPrincipal: css('button[type="submit"]', 'backgroundColor'),
    fondo: css('body', 'backgroundColor'),
    tinta: css('.titulo', 'color'),
    sello: css('.sello', 'color'),
  }
})
comprobar('el boton principal es el amarillo de la marca',
  colores.botonPrincipal === 'rgb(246, 200, 30)', colores.botonPrincipal)
comprobar('el fondo es la crema de la marca', colores.fondo === 'rgb(253, 248, 231)', colores.fondo)

// --- la fuente: Montserrat esta en el CSS, pero Y si el navegador la tiene?
// OJO: document.fonts.check('16px Montserrat') devuelve TRUE tambien para una fuente
// que no existe, porque "no hay nada que cargar" cuenta como "cargado". Es el mismo
// error de dar por bueno un artefacto en vez de lo que de verdad importa. Lo que se
// mira es la lista de fuentes que el navegador efectivamente descargo.
const fuente = await movil.evaluate(async () => {
  await document.fonts.ready
  const cargadas = [...document.fonts]
    .filter((f) => f.status === 'loaded' && /montserrat/i.test(f.family))
    .map((f) => `${f.weight}`)
  return { declarada: getComputedStyle(document.body).fontFamily, pesos: cargadas }
})
console.log('     fuente declarada:', fuente.declarada)
comprobar('Montserrat se descargo de verdad (no solo esta declarada)',
  fuente.pesos.length > 0,
  fuente.pesos.length
    ? `pesos cargados: ${fuente.pesos.join(', ')}`
    : 'ninguno: se ve la fuente de respaldo del sistema')

// --- volver a la busqueda no la reinicia
await movil.getByRole('navigation').getByRole('button', { name: /^entrar$/i }).click()
await movil.getByRole('heading', { name: /^entrar$/i }).waitFor()
await movil.screenshot({ path: resolve(carpeta, '03-entrar-movil.png'), fullPage: true })
await movil.getByRole('navigation').getByRole('button', { name: /^buscar$/i }).click()
comprobar('al volver, los resultados siguen ahi',
  (await movil.locator('.resultado').count()) === 5)

// ------------------------------------------------------------------ escritorio
const escritorio = await nuevaPagina(1280, 800)
await escritorio.goto('https://localhost:5173/', { waitUntil: 'networkidle' })
await escritorio.getByRole('option', { name: 'Corretaje de propiedades' }).waitFor({ state: 'attached' })
await buscarCorretajeEnElCentro(escritorio)
await escritorio.screenshot({ path: resolve(carpeta, '04-resultados-escritorio.png'), fullPage: true })
const ancho = await escritorio.locator('.buscador').boundingBox()
comprobar('en escritorio la columna no se estira de mas', ancho.width <= 641, `${Math.round(ancho.width)} px`)

// ------------------------------------------------------------------ con ubicacion
// Con punto se ordena por TRAMO de distancia (el kilometro entero que se muestra) y,
// dentro del tramo, por calidad. Gestion Labranza cubre el Centro pero su zona principal
// es Labranza, a ~14,5 km: con punto sale ULTIMO, y por zona salia segundo.
const conPunto = await nuevaPagina(390, 844, {
  geolocation: { latitude: -38.7359, longitude: -72.5904 },
  permissions: ['geolocation'],
})
await conPunto.goto('https://localhost:5173/', { waitUntil: 'networkidle' })
await conPunto.getByRole('option', { name: 'Corretaje de propiedades' }).waitFor({ state: 'attached' })
await conPunto.getByLabel(/qué necesitas/i).selectOption('corretaje')
await conPunto.getByRole('button', { name: /usar mi ubicación/i }).click()
await conPunto.getByText(/usando tu ubicación/i).waitFor({ timeout: 10000 })
await conPunto.getByRole('button', { name: /^busca/i }).click()
await conPunto.locator('.resultado').first().waitFor({ timeout: 15000 })
await conPunto.screenshot({ path: resolve(carpeta, '05-resultados-con-ubicacion.png'), fullPage: true })

const nombresPunto = await conPunto.locator('.resultado-nombre').allTextContents()
const metaPunto = await conPunto.locator('.resultado-meta').allTextContents()
console.log('     con ubicacion:', nombresPunto.map((n) => n.slice(0, 16)).join(' > '))
console.log('     distancias:   ', metaPunto.join(' | '))
// Con el radio por omision (10 km) Gestion Labranza queda FUERA: cubre el Centro, pero su
// zona principal esta a ~14,5 km. Es RN-S3-04 aplicada con coherencia, y trae una
// consecuencia que conviene tener a la vista: la pantalla no tiene control de radio, asi
// que quien busca con su ubicacion nunca lo vera ni puede ampliar.
comprobar('con ubicacion y radio de 10 km salen 4: el que se basa a 14,5 km queda fuera',
  nombresPunto.length === 4 && !nombresPunto.some((n) => /labranza/i.test(n)),
  nombresPunto.length + ' resultados')
comprobar('con ubicacion hay distancia redondeada y no hay aviso de "sin distancia"',
  metaPunto.every((m) => /a (menos de 1|\d+) km/.test(m))
  && (await conPunto.locator('.aviso--info').count()) === 0)

// Ampliando el radio a 20 km (por la API, porque la pantalla no tiene el control), el que
// se basa lejos SI aparece, y ultimo: el orden es por tramo de distancia.
const ampliada = await (await conPunto.request.get(
  'https://localhost:5173/api/busqueda?rubro=corretaje&lat=-38.7359&lon=-72.5904&radio=20')).json()
const ordenAmpliado = ampliada.resultados.map((r) => r.nombrePublico)
console.log('     radio 20 km:  ', ordenAmpliado.map((n) => n.slice(0, 16)).join(' > '),
  '|', ampliada.resultados.at(-1)?.distancia)
comprobar('con radio de 20 km aparece el lejano, y ultimo',
  ordenAmpliado.length === 5 && ordenAmpliado.at(-1).startsWith('Gestión Inmobiliaria Labranza'))
const kmLejano = Number(/a (\d+) km/.exec(ampliada.resultados.at(-1)?.distancia ?? '')?.[1])
// Plaza -> centroide de Labranza son 14,5 km (haversine calculado aparte), o sea "a 15 km".
// La primera version de esta comprobacion esperaba ~13: era mala aritmetica de memoria, y
// fue el servidor quien tenia razon.
comprobar('y dice 15 km, que es lo que da la cuenta hecha aparte', kmLejano === 15, `a ${kmLejano} km`)
const htmlPunto = await conPunto.content()
comprobar('con ubicacion tampoco se filtran coordenadas',
  !/@zass\.local|latitud|longitud|-38\.7|-72\.5/i.test(htmlPunto.replace(/<option[^>]*>[^<]*<\/option>/g, '')))

comprobar('sin errores de pagina ni de consola', errores.length === 0, errores.join(' || '))

await navegador.close()
console.log(fallos === 0 ? '\nTodo en orden.' : `\n${fallos} comprobacion(es) en rojo.`)
process.exitCode = fallos === 0 ? 0 : 1
