/**
 * «Mis reseñas» (/mis-resenas, API-65) en un navegador real. Es la pantalla que nadie habia visto
 * NUNCA: la rama que la construyo llevaba dias sin fusionar, y sus pruebas corren en jsdom, donde
 * no hay CSS ni layout ni peticiones de verdad.
 *
 * Ojo con lo que es: son las resenas que YO ESCRIBI, con su ventana de edicion de 72 h (CS-16), no
 * las que un prestador ha recibido. El documento lo decia al reves hasta el 13-nov-2026.
 *
 * Monta el escenario sobre la gente de sembrar-demo.sql y lo deja todo como estaba. A la prestadora
 * de demostracion NO se le toca ni el rol ni la contrasena (ver D9: hubo guiones que se los pisaban
 * al «limpiar» y deshacian el arreglo en silencio).
 *
 *   node pruebas-navegador/mirar-resenas.mjs [carpeta-de-capturas]     (desde frontend/)
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
const correoAutor = `resenadora.${Date.now()}@zass.local`
const clave = 'una-clave-bien-larga'
const COMENTARIO = 'Llego a la hora, explico todo con calma y dejo el trabajo impecable.'

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

/**
 * El valor de la primera columna de la primera fila. `psql -At` con un INSERT ... RETURNING
 * devuelve TAMBIEN la linea «INSERT 0 1», y pasarla entera a la siguiente consulta da un
 * «invalid input syntax for type uuid» que parece un fallo de la aplicacion y es del arnes.
 */
function sqlValor(consulta) {
  return sql(consulta).split('\n')[0].trim()
}

let fallos = 0
function comprobar(texto, bien, detalle = '') {
  console.log(`${bien ? 'ok  ' : 'MAL '} ${texto}${detalle ? ` — ${detalle}` : ''}`)
  if (!bien) { fallos++; process.exitCode = 1 }
}

const errores = []
const fallidas = []
const idPrestador = sql(`SELECT id FROM perfiles_prestador WHERE slug = '${SLUG}'`)

const navegador = await chromium.launch()
const contexto = await navegador.newContext({
  ignoreHTTPSErrors: true, viewport: { width: 1280, height: 900 }, locale: 'es-CL',
})
const pagina = await contexto.newPage()
pagina.on('console', (m) => { if (m.type() === 'error') errores.push(m.text()) })
pagina.on('pageerror', (e) => errores.push(`pageerror: ${e.message}`))
pagina.on('response', (r) => {
  if (r.status() >= 400) fallidas.push(`${r.status()} ${r.request().method()} ${r.url()}`)
})

const foto = (n) => pagina.screenshot({ path: resolve(carpeta, `resenas-${n}.png`), fullPage: true })

try {
  // --- La autora: una cuenta de verdad, creada por la API ----------------------------------
  const registro = await contexto.request.post(`${BASE}/api/auth/registro`, {
    data: { correo: correoAutor, contrasena: clave, nombre: 'Rosa Resenadora', rol: 'buscador' },
  })
  comprobar('la cuenta de la autora se crea', registro.status() === 201, `HTTP ${registro.status()}`)
  sql(`UPDATE usuarios SET correo_verificado = true WHERE correo::text = '${correoAutor}'`)
  const idAutor = sql(`SELECT id FROM usuarios WHERE correo::text = '${correoAutor}'`)

  await pagina.goto(`${BASE}/entrar`, { waitUntil: 'networkidle' })
  await pagina.getByLabel('Correo').fill(correoAutor)
  await pagina.getByLabel('Contraseña').fill(clave)
  await pagina.getByRole('button', { name: /^entrar$/i }).click()
  await pagina.waitForTimeout(3000)

  // --- Sin reseñas: la pantalla tiene que decir algo, no quedarse en blanco ----------------
  await pagina.goto(`${BASE}/mis-resenas`, { waitUntil: 'networkidle' })
  await pagina.getByRole('heading', { level: 1 }).waitFor({ timeout: 20000 })
  await foto('01-vacia')
  // Se mira el CUERPO de la pagina, no el body entero: la cabecera y el pie llevan la palabra
  // «reseña» por ahi y harian pasar esta comprobacion con la pantalla en blanco.
  const cuerpoVacio = await pagina.locator('.ficha-pagina, main').first().innerText()
  comprobar(
    'sin resenas, la pantalla lo dice en vez de quedarse vacia',
    /todav|no has|ninguna|aun no/i.test(cuerpoVacio),
    cuerpoVacio.split('\n').filter(Boolean).slice(0, 3).join(' | '),
  )

  // --- Con una reseña de verdad en la base -------------------------------------------------
  // La escribe la autora sobre la prestadora de demostracion. El contacto es obligatorio (solo
  // resena quien tuvo trato), asi que se siembra tambien.
  const idContacto = sqlValor(`
    INSERT INTO contactos (id, buscador_id, perfil_prestador_id, origen, canal, ocurrido_en)
    VALUES (gen_random_uuid(), '${idAutor}', '${idPrestador}', 'Ficha', 'WhatsApp', now() - interval '2 days')
    RETURNING id`)
  sql(`
    INSERT INTO resenas (id, contacto_id, perfil_prestador_id, autor_id, calificacion, comentario,
                         tipo, estado, aspectos, editada, publicada_en, editable_hasta)
    VALUES (gen_random_uuid(), '${idContacto}', '${idPrestador}', '${idAutor}', 5,
            '${COMENTARIO}', 'Servicio', 'Publicada', ARRAY['Puntualidad','Trato'],
            false, now() - interval '1 hour', now() + interval '71 hours')`)

  await pagina.reload({ waitUntil: 'networkidle' })
  await pagina.waitForTimeout(1500)
  await foto('02-con-una-resena')
  const texto = await pagina.locator('main, body').first().innerText()
  comprobar(
    'la resena escrita aparece en la pantalla',
    texto.includes(COMENTARIO.slice(0, 40)),
    texto.split('\n').filter(Boolean).slice(1, 5).join(' | '),
  )
  comprobar('se dice a quien se la escribio', /Marcela/i.test(texto))

  // CS-16: dentro de las 72 h se puede editar. Si el boton no esta, la ventana no existe.
  const hayEditar = (await pagina.getByRole('button', { name: /editar/i }).count()) > 0
  comprobar('dentro de las 72 h se ofrece editarla (CS-16)', hayEditar,
    hayEditar ? '' : 'no hay ningun boton de editar')

  // --- Y pasado el plazo, NO ---------------------------------------------------------------
  sql(`UPDATE resenas SET editable_hasta = now() - interval '1 hour' WHERE autor_id = '${idAutor}'`)
  await pagina.reload({ waitUntil: 'networkidle' })
  await pagina.waitForTimeout(1500)
  const sigueEditar = (await pagina.getByRole('button', { name: /editar/i }).count()) > 0
  comprobar('pasadas las 72 h ya no se ofrece editarla', !sigueEditar,
    sigueEditar ? 'el boton sigue ahi con el plazo vencido' : '')
  const textoVencido = await pagina.locator('main, body').first().innerText()
  comprobar('la resena sigue viendose aunque ya no se pueda editar',
    textoVencido.includes(COMENTARIO.slice(0, 40)))
  await foto('03-plazo-vencido')

  // --- A ancho de telefono -----------------------------------------------------------------
  await pagina.setViewportSize({ width: 390, height: 844 })
  await pagina.reload({ waitUntil: 'networkidle' })
  await pagina.waitForTimeout(1500)
  const m = await pagina.evaluate(
    () => ({ doc: document.documentElement.scrollWidth, ven: window.innerWidth }))
  comprobar('«Mis resenas» a 390 px no desborda', m.doc <= m.ven + 1, `${m.doc} vs ${m.ven}`)
  await foto('04-movil')
} catch (e) {
  comprobar('el recorrido se completo', false, e.message.split('\n').slice(0, 3).join(' | '))
  await foto('99-error')
} finally {
  console.log('--- errores de consola ---')
  for (const e of new Set(errores)) console.log('  ', e.slice(0, 220))
  console.log('--- peticiones fallidas ---')
  for (const f of new Set(fallidas)) console.log('  ', f.slice(0, 220))
  console.log(`--- comprobaciones en rojo: ${fallos} ---`)

  sql(`DELETE FROM resenas WHERE autor_id = (SELECT id FROM usuarios WHERE correo::text = '${correoAutor}')`)
  sql(`DELETE FROM contactos WHERE buscador_id = (SELECT id FROM usuarios WHERE correo::text = '${correoAutor}')`)
  sql(`DELETE FROM usuarios WHERE correo::text = '${correoAutor}'`)
  console.log('limpieza: resena, contacto y cuenta de la autora borrados')
  await navegador.close()
}
