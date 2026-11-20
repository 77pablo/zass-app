/**
 * JQ-5 en un navegador real: tocar WhatsApp en la ficha registra el contacto (API-46), y eso, pasados
 * 7 dias, habilita la reseña. En jsdom no hay peticiones de verdad ni pestañas: esto si.
 *
 * A DIFERENCIA de los otros guiones, este NO corre psql ni lee la clave de la base. Lo que haya
 * que hacer en la base (verificar el correo, mover la fecha del contacto, limpiar) lo imprime, y
 * lo corre una persona entre una fase y la siguiente.
 *
 * ANTES: la base local sembrada con sembrar-demo.sql CON TELEFONO, para que marcela-soto tenga
 * WhatsApp. Con el sembrado de JQ-6 (PR #82) es opcional y va con un numero tuyo:
 *     psql ... -v tel_demo="+56 9 ..." -f pruebas-navegador/datos/sembrar-demo.sql
 * Sin eso la fase 2 se detiene sola y lo dice. API e interfaz levantadas (.\ver-zass.bat).
 *
 *   node pruebas-navegador/mirar-contacto.mjs cuenta                  (desde frontend/)
 *   node pruebas-navegador/mirar-contacto.mjs contacto <correo>
 *   node pruebas-navegador/mirar-contacto.mjs resena   <correo>
 *
 * El numero de telefono no sale en ningun lado: las capturas tapan la caja de contacto y por
 * consola solo se imprime el origen, el canal y el estado HTTP de cada registro.
 */
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'
import { chromium } from 'playwright'

const BASE = process.env.ZASS_BASE ?? 'https://localhost:5173'
const raiz = resolve(import.meta.dirname, '..', '..')
const carpeta = resolve(raiz, 'registros', 'capturas')
mkdirSync(carpeta, { recursive: true })

const SLUG = 'marcela-soto'
const CLAVE = 'una-clave-bien-larga'
const [fase, correoArg] = process.argv.slice(2)

let fallos = 0
function comprobar(texto, bien, detalle = '') {
  console.log(`${bien ? 'ok  ' : 'MAL '} ${texto}${detalle ? ` — ${detalle}` : ''}`)
  if (!bien) { fallos++; process.exitCode = 1 }
}

/** Se detiene sin haber registrado nada: lo unico que queda por limpiar es la cuenta. */
function detener(mensaje, correo) {
  console.error(`\nDETENIDO: ${mensaje}`)
  if (correo) console.error(`\nLimpieza (la corres tu):\n${sqlLimpieza(correo)}`)
  process.exit(2)
}

const deQuien = (correo) => `(SELECT id FROM usuarios WHERE correo::text = '${correo}')`
const sqlLimpieza = (correo) =>
  `DELETE FROM contactos WHERE buscador_id = ${deQuien(correo)};\n` +
  `DELETE FROM usuarios WHERE correo::text = '${correo}';`

if (!['cuenta', 'contacto', 'resena'].includes(fase) || (fase !== 'cuenta' && !correoArg)) {
  console.error('Uso: mirar-contacto.mjs cuenta | contacto <correo> | resena <correo>')
  process.exit(64)
}

const navegador = await chromium.launch()
const contexto = await navegador.newContext({
  ignoreHTTPSErrors: true, viewport: { width: 1280, height: 900 }, locale: 'es-CL',
})
// WhatsApp de verdad nunca se abre: la pestaña nueva se cierra y la peticion a wa.me se corta.
await contexto.route('https://wa.me/**', (r) => r.abort())
const pagina = await contexto.newPage()
contexto.on('page', (p) => { if (p !== pagina) void p.close() })
// `tel:` y `mailto:` saldrian de la pagina hacia otra aplicacion. Se les quita el viaje, no el
// clic: este oyente va en fase de captura en `window`, antes que React, y no corta la propagacion,
// asi que el onClick de la ficha corre igual.
await pagina.addInitScript(() => {
  window.addEventListener('click', (e) => {
    const a = e.target instanceof Element ? e.target.closest('a') : null
    if (a && /^(tel|mailto):/i.test(a.getAttribute('href') ?? '')) e.preventDefault()
  }, true)
})

const errores = []
const fallidas = []
const registros = []
pagina.on('console', (m) => { if (m.type() === 'error') errores.push(m.text()) })
pagina.on('pageerror', (e) => errores.push(`pageerror: ${e.message}`))
pagina.on('response', async (r) => {
  const url = new URL(r.url())
  if (url.pathname === '/api/contactos' && r.request().method() === 'POST') {
    const enviado = JSON.parse(r.request().postData() ?? '{}')
    let id = null
    try { id = (await r.json()).id ?? null } catch { /* sin cuerpo */ }
    registros.push({ estado: r.status(), origen: enviado.origen, canal: enviado.canal, id })
  } else if (r.status() >= 400 && url.origin === new URL(BASE).origin) {
    fallidas.push(`${r.status()} ${r.request().method()} ${url.pathname}`)
  }
})

const foto = (n) => pagina.screenshot({
  path: resolve(carpeta, `contacto-${n}.png`), fullPage: true, mask: [pagina.locator('.contacto')],
})

async function entrar(correo) {
  await pagina.goto(`${BASE}/entrar`, { waitUntil: 'networkidle' })
  await pagina.getByLabel('Correo').fill(correo)
  await pagina.getByLabel('Contraseña').fill(CLAVE)
  await pagina.getByRole('button', { name: /^entrar$/i }).click()
  await pagina.waitForTimeout(3000)
}

async function abrirFicha() {
  await pagina.goto(`${BASE}/prestadores/${SLUG}`, { waitUntil: 'networkidle' })
  await pagina.getByRole('heading', { level: 1 }).waitFor({ timeout: 20000 })
}

/** Espera a que haya `n` registros, o se rinde a los `ms`. */
async function hastaRegistros(n, ms = 6000) {
  const fin = Date.now() + ms
  while (registros.length < n && Date.now() < fin) await pagina.waitForTimeout(100)
}

try {
  if (fase === 'cuenta') {
    const correo = `contacto.${Date.now()}@zass.local`
    const r = await contexto.request.post(`${BASE}/api/auth/registro`, {
      data: { correo, contrasena: CLAVE, nombre: 'Rosa Contacto', rol: 'buscador' },
    })
    comprobar('la cuenta de buscador se crea por la API', r.status() === 201, `HTTP ${r.status()}`)
    console.log(`\nCorreo de la cuenta: ${correo}`)
    console.log(`\nSQL-1 (lo corres tu), y despues la fase «contacto ${correo}»:`)
    // Lo mismo que hace Usuario.VerificarCorreo: marcar el correo Y activar la cuenta. Solo el
    // correo deja la cuenta en PendienteVerificacion, y para reseñar (y registrar contactos)
    // hace falta Activa: API-46 responde 403 «no elegible» y la ficha no registra nada.
    console.log(`UPDATE usuarios SET correo_verificado = true, estado = CASE WHEN estado = 'PendienteVerificacion' THEN 'Activa' ELSE estado END WHERE correo::text = '${correo}';`)
  }

  if (fase === 'contacto') {
    const correo = correoArg
    await entrar(correo)
    await abrirFicha()

    // --- Antes de tocar nada: ¿la ficha tiene WhatsApp? ------------------------------------
    const caja = pagina.locator('aside[aria-label="Contacto"]')
    const textoCaja = await caja.innerText()
    if (await caja.getByRole('link', { name: /WhatsApp/ }).count() === 0) {
      if (/no está recibiendo contactos/i.test(textoCaja)) {
        detener(`la suscripción de ${SLUG} no está vigente; vuelve a sembrar.`, correo)
      }
      if (/verifica tu correo/i.test(textoCaja)) {
        detener('el correo de la cuenta no está verificado: falta correr SQL-1.', correo)
      }
      if (/crea tu cuenta|Entrar/i.test(textoCaja)) {
        detener('no se pudo entrar con la cuenta: revisa el correo de la fase «cuenta».', correo)
      }
      detener(
        `${SLUG} no tiene WhatsApp. Siembra con -v tel_demo="+56 9 ..." ` +
        '(sembrar-demo.sql de PR #82) y vuelve a correr esta fase.', correo)
    }
    await foto('01-ficha')

    // --- WhatsApp registra, una vez --------------------------------------------------------
    await caja.getByRole('link', { name: /WhatsApp/ }).click()
    await hastaRegistros(1)
    const [primero] = registros
    comprobar('tocar WhatsApp registra el contacto', registros.length === 1, `${registros.length} registro(s)`)
    if (primero) {
      comprobar('con origen Ficha y canal Whatsapp',
        primero.origen === 'Ficha' && primero.canal === 'Whatsapp', `${primero.origen} | ${primero.canal}`)
      comprobar('y la API responde 200', primero.estado === 200, `HTTP ${primero.estado}`)
    }

    // --- Los demas canales, si estan, no vuelven a llamar en la misma visita -----------------
    for (const [canal, nombre] of [['llamar', /Llamar/], ['correo', /Escribir un correo/], ['sitio web', /sitio web/]]) {
      const enlace = caja.getByRole('link', { name: nombre })
      if (await enlace.count() === 0) { console.log(`omitido: sin ${canal}`); continue }
      await enlace.click()
    }
    await pagina.waitForTimeout(1500)
    comprobar('los demás toques de la misma visita no registran otra vez', registros.length === 1,
      `${registros.length} registro(s)`)

    // --- Otra visita: vuelve a llamar, y el servidor devuelve el mismo contacto ------------
    await abrirFicha()
    await caja.getByRole('link', { name: /WhatsApp/ }).click()
    await hastaRegistros(2)
    comprobar('en una visita nueva vuelve a llamar una vez', registros.length === 2, `${registros.length} registro(s)`)
    if (registros.length === 2 && primero) {
      comprobar('y el servidor devuelve el mismo contacto, no uno nuevo',
        registros[1].id !== null && registros[1].id === primero.id)
    }
    await foto('02-tras-whatsapp')

    // --- La reseña todavia no: faltan 7 dias ----------------------------------------------
    await pagina.goto(`${BASE}/prestadores/${SLUG}/resena`, { waitUntil: 'networkidle' })
    await pagina.getByRole('heading', { level: 1 }).waitFor({ timeout: 20000 })
    const cuerpo = await pagina.locator('main').first().innerText()
    comprobar('la reseña dice que faltan los 7 días, con fecha', /7 días/.test(cuerpo) && /desde el \d/.test(cuerpo),
      cuerpo.split('\n').filter(Boolean).slice(0, 3).join(' | '))
    await foto('03-resena-espera')

    console.log('\nSQL-2 (lo corres tu), y despues la fase «resena»:')
    console.log(`SELECT origen, canal, ocurrido_en FROM contactos WHERE buscador_id = ${deQuien(correo)};  -- espero 1 fila: Ficha | Whatsapp`)
    console.log(`UPDATE contactos SET ocurrido_en = ocurrido_en - interval '8 days' WHERE buscador_id = ${deQuien(correo)};`)
  }

  if (fase === 'resena') {
    const correo = correoArg
    await entrar(correo)
    await pagina.goto(`${BASE}/prestadores/${SLUG}/resena`, { waitUntil: 'networkidle' })
    await pagina.getByRole('heading', { level: 1 }).waitFor({ timeout: 20000 })
    // NO se publica: se mira que el formulario aparezca, nada mas.
    comprobar('pasados los 7 días, aparece el formulario de la reseña',
      await pagina.getByRole('heading', { name: /cómo te fue/i }).count() === 1)
    comprobar('y ya no dice que no puedes reseñar',
      await pagina.getByRole('heading', { name: /todavía no puedes reseñar/i }).count() === 0)
    await foto('04-resena-habilitada')

    console.log('\nSQL-3, limpieza (la corres tu):')
    console.log(sqlLimpieza(correo))
  }
} catch (e) {
  comprobar('el recorrido se completó', false, e.message.split('\n').slice(0, 3).join(' | '))
  await foto('99-error').catch(() => {})
} finally {
  console.log('--- errores de consola ---')
  for (const e of new Set(errores)) console.log('  ', e.slice(0, 220))
  console.log('--- peticiones fallidas ---')
  for (const f of new Set(fallidas)) console.log('  ', f.slice(0, 220))
  console.log(`--- comprobaciones en rojo: ${fallos} ---`)
  await navegador.close()
}
