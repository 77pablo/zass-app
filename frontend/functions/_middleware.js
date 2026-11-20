/*
 * Mantenimiento: el sitio cerrado hasta que se abra de verdad.
 *
 * Esto vive en `functions/_middleware.js` porque en Cloudflare Pages ESE archivo corre
 * ANTES que todo lo demas y para TODA peticion: las paginas, los recursos estaticos, las
 * Functions de la ficha y del mapa del sitio, y el proxy de la API. Un interruptor en un
 * solo lugar, y no uno por ruta que alguien va a olvidar.
 *
 * Esta ENCENDIDO por omision, a proposito. Mientras ZASS! no este listo, lo peligroso es
 * que el sitio quede abierto por olvidar poner una variable; que quede cerrado por olvidar
 * quitarla no le cuesta nada a nadie. Para abrirlo hay que decirlo en voz alta:
 *
 *     MANTENIMIENTO = "0"     en las variables del proyecto en Cloudflare Pages
 *
 * Y para mirar el sitio de verdad mientras sigue cerrado, un pase:
 *
 *     MANTENIMIENTO_PASE = "<algo largo y que no se adivine>"
 *     despues se entra una vez a  https://zass.cl/?pase=<ese valor>
 *
 * Eso deja una cookie por 8 horas y quien la tenga ve el sitio completo. Sin la variable
 * puesta, el pase no existe y no hay forma de entrar: no hay puerta de atras por omision.
 *
 * Responde 503 y no 200 por una razon concreta: a Google un 503 le dice «vuelve despues»
 * y no indexa nada, mientras que un 200 con la palabra «mantenimiento» le dice que ESE es
 * el contenido del sitio, y eso es lo que queda guardado en el buscador durante semanas.
 */

import { conCabecerasDeSeguridad } from './_comun/cabeceras.js'

const VARIABLE_INTERRUPTOR = 'MANTENIMIENTO'
const VARIABLE_PASE = 'MANTENIMIENTO_PASE'
const COOKIE_PASE = 'zass_pase'
const HORAS_DE_PASE = 8
const PARAMETRO_PASE = 'pase'

/** Segundos que se le piden a los buscadores y a los navegadores antes de volver. */
const REINTENTAR_EN = 3600

/** TEXTOS PROPUESTOS, pendientes de aprobacion de Pablo (CONTRIBUIR.md, regla 12). */
const TEXTOS = {
  titulo: 'ZASS! · Volvemos muy pronto',
  encabezado: 'Estamos terminando de construir ZASS!',
  cuerpo:
    'Le estamos dando los últimos detalles a la plataforma. Volvemos muy pronto, y cuando '
    + 'abramos vas a poder encontrar al profesional o al maestro que necesitas, cerca de ti.',
  prestadores:
    '¿Ofreces un servicio y quieres estar entre los primeros en aparecer? Escríbenos y te '
    + 'avisamos el día que abrimos.',
  correo: 'somoszaschile@gmail.com',
  pie: 'Conecta con un toque',
}

/** El sitio esta cerrado salvo que alguien lo haya abierto diciendo MANTENIMIENTO=0. */
function enMantenimiento(env) {
  const valor = (env?.[VARIABLE_INTERRUPTOR] ?? '').toString().trim().toLowerCase()
  return !(valor === '0' || valor === 'false' || valor === 'no')
}

/**
 * Comparacion que no delata el largo ni los caracteres acertados por el tiempo que tarda.
 * Es un pase, no una contrasena, pero comparar secretos con === es una costumbre que no
 * conviene tener.
 */
function sonIguales(a, b) {
  if (typeof a !== 'string' || typeof b !== 'string') return false
  if (a.length !== b.length) return false
  let diferencia = 0
  for (let i = 0; i < a.length; i += 1) diferencia |= a.charCodeAt(i) ^ b.charCodeAt(i)
  return diferencia === 0
}

function cookieDelPase(cabecera) {
  if (typeof cabecera !== 'string') return null
  for (const trozo of cabecera.split(';')) {
    const [nombre, ...resto] = trozo.trim().split('=')
    if (nombre === COOKIE_PASE) return resto.join('=')
  }
  return null
}

/*
 * El icono de la marca, el mismo de public/icono-zass.svg (#100): Z pizarra y el «!» en un sello
 * ambar. Va ESCRITO aqui y no como <img src="/icono-zass.svg"> porque, con el sitio cerrado, este
 * middleware tambien contesta /icono-zass.svg... con esta misma pagina. Nada de la pagina puede
 * venir del propio sitio: lo vigila mantenimiento-guia-v3.prueba.js.
 */
const ICONO_Z = 'M120 128H352V184L200 328H352V384H120V328L272 184H120Z'
const ICONO = `<svg viewBox="0 0 512 512" width="56" height="56" aria-hidden="true" focusable="false">`
  + '<rect width="512" height="512" rx="112" fill="#00A6FF"/>'
  + '<g transform="translate(-40 -44)">'
  + `<path d="${ICONO_Z}" fill="#111827"/>`
  + '<circle cx="372" cy="372" r="92" fill="#FFB800" stroke="#00A6FF" stroke-width="16"/>'
  + '<rect x="358" y="314" width="28" height="70" rx="14" fill="#111827"/>'
  + '<circle cx="372" cy="414" r="15" fill="#111827"/>'
  + '</g></svg>'

/** El mismo icono como favicon: un data: URI (la CSP permite img-src data:). */
const FAVICON = `data:image/svg+xml,${encodeURIComponent(
  `<svg xmlns="http://www.w3.org/2000/svg"${ICONO.slice(4).replace(' width="56" height="56" aria-hidden="true" focusable="false"', '')}`,
)}`

/*
 * La pagina, con la guia v3 (aprobada por Pablo el 17-nov-2026): blanco y pizarra de base, una
 * franja celeste de identidad con texto pizarra (6,7:1), el ambar solo en el boton del correo
 * (pizarra encima: 10,2:1) y el pie como escena pizarra. Sin degradados, sin naranja ni rojo.
 * Primero el celular: todo en una columna, el boton a todo el ancho y de 48 px de alto.
 * Los colores son los mismos tokens de src/index.css, con el mismo nombre.
 */
function paginaDeMantenimiento() {
  return `<!doctype html>
<html lang="es-CL">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="robots" content="noindex, nofollow">
<meta name="theme-color" content="#00A6FF">
<meta name="color-scheme" content="light">
<title>${TEXTOS.titulo}</title>
<link rel="icon" type="image/svg+xml" href="${FAVICON}">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Montserrat:wght@500;700;800&display=swap" rel="stylesheet">
<style>
  :root {
    --blanco: #ffffff; --pizarra: #111827; --suave: #4b5563; --gris: #f3f4f6;
    --linea-fuerte: #d1d5db; --celeste: #00a6ff; --ambar: #ffb800; --ambar-hondo: #f0ab00;
    color-scheme: light;
  }
  * { box-sizing: border-box; }
  html { -webkit-text-size-adjust: 100%; }
  body {
    margin: 0; min-height: 100vh; min-height: 100dvh; display: flex; flex-direction: column;
    background: var(--blanco); color: var(--pizarra);
    font-family: Montserrat, system-ui, -apple-system, "Segoe UI", sans-serif;
    font-weight: 500; line-height: 1.6; -webkit-tap-highlight-color: transparent;
  }
  .franja {
    background: var(--celeste); color: var(--pizarra);
    padding: calc(20px + env(safe-area-inset-top)) max(16px, env(safe-area-inset-right)) 20px max(16px, env(safe-area-inset-left));
  }
  /* La franja, el texto y el pie arrancan en la misma columna: 34rem de contenido + 16px por lado. */
  .franja-dentro, .pie-dentro { width: 100%; max-width: 34rem; margin: 0 auto; }
  main { width: min(100%, calc(34rem + 32px)); margin: 0 auto; }
  .franja-dentro { display: flex; align-items: center; gap: 12px; }
  .franja svg { display: block; flex: none; width: 56px; height: 56px; border-radius: 14px; box-shadow: 0 0 0 2px var(--pizarra); }
  .marca { margin: 0; font-size: 2rem; font-weight: 800; letter-spacing: -0.02em; line-height: 1; display: flex; align-items: center; }
  .sello {
    display: inline-grid; place-items: center; width: 1.1em; height: 1.1em; margin-left: 0.06em;
    border-radius: 50%; background: var(--ambar); color: var(--pizarra); font-size: 0.8em;
  }
  main { flex: 1; padding: 32px max(16px, env(safe-area-inset-right)) 40px max(16px, env(safe-area-inset-left)); }
  h1 { font-size: clamp(1.5rem, 6.4vw, 2rem); font-weight: 800; line-height: 1.2; letter-spacing: -0.01em; margin: 0 0 16px; text-wrap: balance; }
  p { margin: 0 0 16px; }
  .cuerpo { color: var(--suave); font-size: 1.0625rem; text-wrap: pretty; }
  .destacado {
    background: var(--gris); border: 1px solid var(--linea-fuerte); border-radius: 16px;
    padding: 20px; margin-top: 28px;
  }
  .destacado p { text-wrap: pretty; }
  .correo {
    display: flex; align-items: center; justify-content: center; min-height: 48px; width: 100%;
    padding: 12px 20px; border-radius: 12px; background: var(--ambar); color: var(--pizarra);
    font-weight: 700; text-decoration: none; overflow-wrap: anywhere; text-align: center;
    touch-action: manipulation; transition: background-color 150ms ease;
  }
  .correo:hover { background: var(--ambar-hondo); text-decoration: underline; }
  .correo:focus-visible { outline: 3px solid var(--pizarra); outline-offset: 3px; }
  .pie {
    background: var(--pizarra); color: var(--blanco); font-weight: 700; font-size: 0.9375rem;
    padding: 20px max(16px, env(safe-area-inset-right)) calc(20px + env(safe-area-inset-bottom)) max(16px, env(safe-area-inset-left));
  }
  /* En un celular de 320 px el correo no cabe en una linea con 20 px de relleno y se partia en «.c / om». */
  @media (max-width: 359px) {
    .destacado { padding: 16px; }
    .correo { padding-left: 12px; padding-right: 12px; font-size: 0.9375rem; }
  }
  @media (min-width: 720px) {
    .franja { padding-top: calc(32px + env(safe-area-inset-top)); padding-bottom: 32px; }
    main { padding-top: 56px; padding-bottom: 64px; }
    h1 { font-size: 2.5rem; }
    .cuerpo { font-size: 1.125rem; }
    .destacado { padding: 24px 28px; }
    .correo { display: inline-flex; width: auto; }
  }
  @media (prefers-reduced-motion: reduce) { .correo { transition: none; } }
</style>
</head>
<body>
<header class="franja">
  <div class="franja-dentro">
    ${ICONO}
    <p class="marca" translate="no">ZASS<span class="sello">!</span></p>
  </div>
</header>
<main>
  <h1>${TEXTOS.encabezado}</h1>
  <p class="cuerpo">${TEXTOS.cuerpo}</p>
  <div class="destacado">
    <p>${TEXTOS.prestadores}</p>
    <a class="correo" href="mailto:${TEXTOS.correo}" translate="no">${TEXTOS.correo}</a>
  </div>
</main>
<footer class="pie"><div class="pie-dentro">${TEXTOS.pie}</div></footer>
</body>
</html>`
}

function respuestaDeMantenimiento(peticion) {
  const cabeceras = {
    'content-type': 'text/html; charset=utf-8',
    // Que ni el navegador ni Cloudflare se queden con esta pagina: el dia que se abra, se
    // abre para todos y no solo para quien limpie su cache.
    'cache-control': 'no-store, must-revalidate',
    'retry-after': String(REINTENTAR_EN),
    'x-robots-tag': 'noindex, nofollow',
  }

  // A una peticion que no es de un navegador (la API, un fetch) no se le manda una pagina
  // HTML: se le dice que el servicio no esta disponible y por que.
  const quiereHtml = (peticion.headers.get('accept') ?? '').includes('text/html')
  if (!quiereHtml) {
    return new Response(
      JSON.stringify({
        title: 'En mantenimiento',
        status: 503,
        detail: 'ZASS! está en mantenimiento. Vuelve a intentarlo más tarde.',
      }),
      {
        status: 503,
        headers: {
          'content-type': 'application/problem+json; charset=utf-8',
          'cache-control': 'no-store, must-revalidate',
          'retry-after': String(REINTENTAR_EN),
          'x-robots-tag': 'noindex, nofollow',
        },
      },
    )
  }

  return new Response(paginaDeMantenimiento(), { status: 503, headers: cabeceras })
}

/** Toda respuesta sale con las cabeceras de seguridad: _headers no cubre a las Functions. */
export async function onRequest(contexto) {
  return conCabecerasDeSeguridad(await decidir(contexto))
}

async function decidir(contexto) {
  const { request, env, next } = contexto

  if (!enMantenimiento(env)) return next()

  const pase = (env?.[VARIABLE_PASE] ?? '').toString()

  // Sin pase configurado no hay puerta de atras, y punto.
  if (pase.length > 0) {
    const url = new URL(request.url)
    const pedido = url.searchParams.get(PARAMETRO_PASE)

    if (pedido !== null && sonIguales(pedido, pase)) {
      // Se guarda en una cookie y se redirige SIN el parametro, para que el pase no quede
      // en el historial, ni en un enlace compartido, ni en los registros de cada recurso.
      url.searchParams.delete(PARAMETRO_PASE)
      return new Response(null, {
        status: 302,
        headers: {
          location: url.pathname + (url.search || '') + url.hash,
          'set-cookie':
            `${COOKIE_PASE}=${pase}; Path=/; Max-Age=${HORAS_DE_PASE * 3600}`
            + '; HttpOnly; Secure; SameSite=Lax',
          'cache-control': 'no-store',
        },
      })
    }

    if (sonIguales(cookieDelPase(request.headers.get('cookie')), pase)) return next()
  }

  return respuestaDeMantenimiento(request)
}
