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
 * Y para cumplir con quien exige un sitio publico con precios y condiciones (Flow) SIN abrir
 * todo, un interruptor aparte, APAGADO por omision:
 *
 *     MANTENIMIENTO_PAGINAS_PUBLICAS = "1"     (tambien vale true o si)
 *
 * Con el, y mientras el sitio siga en mantenimiento, el publico ve SOLO /planes, /terminos y
 * /privacidad (con sus recursos estaticos y GET /api/planes). Todo lo demas sigue en 503.
 * Sin la variable, el comportamiento es exactamente el de siempre.
 *
 * Responde 503 y no 200 por una razon concreta: a Google un 503 le dice «vuelve despues»
 * y no indexa nada, mientras que un 200 con la palabra «mantenimiento» le dice que ESE es
 * el contenido del sitio, y eso es lo que queda guardado en el buscador durante semanas.
 */

import { conCabecerasDeSeguridad } from './_comun/cabeceras.js'

const VARIABLE_INTERRUPTOR = 'MANTENIMIENTO'
const VARIABLE_PASE = 'MANTENIMIENTO_PASE'
const VARIABLE_PAGINAS_PUBLICAS = 'MANTENIMIENTO_PAGINAS_PUBLICAS'
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

/** Paginas que el interruptor de paginas publicas deja ver (rutas exactas, con o sin «/» final). */
const PAGINAS_PUBLICAS = new Set(['/planes', '/terminos', '/privacidad'])

/**
 * Archivos sueltos de la raiz de public/ que esas paginas necesitan. NO esta sw.js, a
 * proposito: durante el mantenimiento no debe instalarse el service worker, porque se
 * quedaria con copias de un sitio a medio abrir y seguiria sirviendolas despues.
 */
const ARCHIVOS_PUBLICOS = new Set([
  '/favicon.svg',
  '/apple-touch-icon.png',
  '/manifest.webmanifest',
  '/og-zass.png',
  '/robots.txt',
  '/icono-zass.svg',
  '/icono-zass-192.png',
  '/icono-zass-512.png',
  '/icono-zass-maskable-512.png',
  '/icono-zass-maskable.svg',
])

/** Solo si vale 1/true/si se abre la lista; sin la variable no cambia nada. */
function paginasPublicasAbiertas(env) {
  const valor = (env?.[VARIABLE_PAGINAS_PUBLICAS] ?? '').toString().trim().toLowerCase()
  return valor === '1' || valor === 'true' || valor === 'si'
}

/**
 * Peticion que el modo de paginas publicas deja pasar. Se mira el pathname TAL COMO VIENE:
 * nada de decodificar, asi que un «%», un «..» o un «//» dejan la ruta fuera. Distingue
 * mayusculas (Pages tambien).
 */
function esPublica(peticion) {
  if (peticion.method !== 'GET' && peticion.method !== 'HEAD') return false
  const { pathname } = new URL(peticion.url)
  if (pathname.includes('%') || pathname.includes('..') || pathname.includes('//')) return false
  if (pathname === '/api/planes') return true
  if (pathname.startsWith('/api/')) return false
  if (pathname.startsWith('/assets/') && pathname.length > '/assets/'.length) return true
  if (ARCHIVOS_PUBLICOS.has(pathname)) return true
  const sinBarra = pathname.length > 1 && pathname.endsWith('/') ? pathname.slice(0, -1) : pathname
  return PAGINAS_PUBLICAS.has(sinBarra)
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
 *
 * `secreto` va SEGUNDO: el recorrido dura siempre su largo, sea cual sea lo que mande el
 * visitante. Antes se cortaba al ver largos distintos, y eso si delataba el largo del pase
 * (revision de seguridad del 25-nov-2026). La diferencia de largos entra en el resultado.
 */
function sonIguales(pedido, secreto) {
  if (typeof pedido !== 'string' || typeof secreto !== 'string') return false
  let diferencia = pedido.length ^ secreto.length
  for (let i = 0; i < secreto.length; i += 1) {
    const delPedido = i < pedido.length ? pedido.charCodeAt(i) : 0
    diferencia |= delPedido ^ secreto.charCodeAt(i)
  }
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
 * El logo de ZASS! (Pablo, 26-nov-2026): la palabra «ZASS!» con las tres ondas, en el amarillo de
 * la marca. Va ESCRITO aqui y no como <img src="..."> porque, con el sitio cerrado, este middleware
 * tambien contesta cualquier archivo del sitio... con esta misma pagina. Nada de la pagina puede
 * venir del propio sitio: lo vigila mantenimiento-guia-v3.prueba.js. Es el contorno del logo que
 * entrego Pablo, pasado a vector: no depende de que cargue ninguna fuente.
 */
const LOGO_TRAZO = 'M2177 500L2164 507L2149 521L2138 542L2136 551L2136 574L2144 596L2151 606L2163 618L2173 624L2189 630L2202 632L2225 631L2249 622L2256 617L2273 598L2281 577L2281 547L2275 530L2257 509L2232 497L2223 495L2195 495ZM2394 206L2382 210L2374 217L2368 227L2366 240L2371 255L2394 278L2411 305L2420 332L2422 346L2422 368L2418 391L2406 419L2389 442L2370 461L2366 479L2371 494L2380 503L2391 508L2405 508L2417 503L2443 477L2458 456L2471 431L2483 391L2485 377L2485 337L2481 314L2469 279L2455 254L2433 226L2417 212L2407 207ZM2465 106L2452 108L2438 118L2432 129L2432 146L2438 158L2464 180L2484 201L2502 225L2517 252L2532 291L2540 333L2540 381L2536 407L2530 430L2520 456L2504 486L2490 506L2471 528L2442 552L2434 562L2431 573L2434 591L2446 604L2457 608L2469 608L2476 606L2501 587L2529 559L2559 518L2579 480L2591 447L2599 416L2604 376L2604 338L2599 299L2587 255L2569 214L2549 181L2522 148L2483 113L2474 108ZM2138 89L2162 439L2256 439L2280 90ZM730 89L491 625L617 625L663 513L668 510L912 510L916 513L962 626L1091 625L852 89ZM789 212L794 218L875 414L874 417L705 416ZM6 89L6 189L292 189L293 192L0 545L0 625L469 625L468 525L164 524L457 170L457 89ZM1797 83L1752 93L1713 110L1682 131L1660 154L1642 184L1634 206L1630 226L1630 270L1637 298L1644 314L1661 337L1677 352L1700 367L1722 378L1754 390L1795 402L1834 410L1887 424L1905 431L1921 440L1934 452L1940 463L1942 471L1942 485L1939 496L1933 506L1921 518L1898 529L1869 535L1825 536L1781 531L1746 522L1703 505L1660 481L1619 572L1622 576L1644 590L1683 608L1727 622L1767 630L1802 634L1856 635L1892 632L1924 626L1965 612L2000 592L2028 567L2044 545L2059 512L2065 479L2063 441L2056 415L2041 389L2023 369L2001 353L1978 341L1959 333L1920 321L1813 295L1795 289L1777 280L1761 267L1754 256L1752 246L1753 228L1761 210L1776 196L1797 186L1811 182L1834 179L1887 180L1920 186L1955 197L2003 220L2015 194L2041 127L2021 115L1989 101L1955 91L1926 85L1877 80L1828 80ZM1291 84L1258 91L1240 97L1205 114L1180 132L1162 150L1146 174L1136 197L1129 229L1129 266L1133 289L1138 304L1154 330L1170 347L1190 362L1221 378L1279 398L1350 414L1386 424L1417 438L1434 453L1440 465L1441 484L1438 496L1425 514L1415 521L1397 529L1368 535L1322 536L1281 531L1242 521L1206 507L1159 481L1118 572L1121 576L1143 590L1182 608L1219 620L1254 628L1299 634L1353 635L1392 632L1424 626L1464 612L1499 592L1527 567L1543 545L1557 515L1563 488L1563 448L1555 415L1543 393L1522 369L1500 353L1475 340L1419 321L1312 295L1294 289L1276 280L1260 267L1253 255L1251 246L1253 224L1261 209L1270 200L1282 192L1303 184L1331 179L1376 179L1414 185L1451 196L1503 220L1540 127L1524 117L1493 103L1462 93L1425 85L1377 80L1330 80ZM2515 0L2502 4L2495 9L2489 17L2486 26L2486 37L2489 46L2495 55L2529 80L2562 111L2593 149L2618 190L2636 230L2647 265L2656 309L2659 353L2656 405L2648 447L2634 490L2614 532L2594 564L2567 598L2531 633L2498 657L2493 662L2487 673L2486 689L2492 702L2499 709L2510 714L2524 714L2544 704L2580 675L2600 656L2641 608L2675 553L2698 500L2713 448L2721 396L2722 337L2719 300L2715 277L2701 223L2683 178L2657 130L2624 85L2588 47L2556 20L2535 5L2527 1Z'
const LOGO = '<svg class="logo" viewBox="0 0 2723 715" role="img" aria-label="ZASS!" focusable="false">'
  + `<path d="${LOGO_TRAZO}" fill="#F6C81E" fill-rule="evenodd"/></svg>`

/** El icono de la pestaña: el mismo dibujo de public/favicon.svg, como data: URI (la CSP permite img-src data:). */
const ICONO_FAVICON = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">'
  + '<rect width="512" height="512" rx="96" fill="#FFFFFF"/>'
  + '<g transform="translate(256 256) scale(1.25) translate(-261 -256)" fill="#F6C81E" stroke="#F6C81E">'
  + '<path d="M110 121H181L169 297H122Z"/><ellipse cx="146" cy="359" rx="37" ry="35"/>'
  + '<g fill="none" stroke-width="32" stroke-linecap="round"><path d="M240 196A78 78 0 0 1 240 316"/>'
  + '<path d="M273 146A138 138 0 0 1 273 366"/><path d="M300 93A196 196 0 0 1 300 419"/></g></g></svg>'
const FAVICON = `data:image/svg+xml,${encodeURIComponent(ICONO_FAVICON)}`

/*
 * La pagina, con la guia v3 (aprobada por Pablo el 17-nov-2026): blanco y pizarra de base, una
 * franja pizarra con el logo amarillo (26-nov: sobre celeste el amarillo no se leia, 1,6:1), el ambar solo en el boton del correo
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
<meta name="theme-color" content="#111827">
<meta name="color-scheme" content="light">
<title>${TEXTOS.titulo}</title>
<link rel="icon" type="image/svg+xml" href="${FAVICON}">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link href="https://fonts.googleapis.com/css2?family=Montserrat:wght@500;700;800&display=swap" rel="stylesheet">
<style>
  :root {
    --blanco: #ffffff; --pizarra: #111827; --suave: #4b5563; --gris: #f3f4f6;
    --linea-fuerte: #d1d5db; --logo: #f6c81e; --ambar: #ffb800; --ambar-hondo: #f0ab00;
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
    background: var(--pizarra); color: var(--blanco);
    padding: calc(20px + env(safe-area-inset-top)) max(16px, env(safe-area-inset-right)) 20px max(16px, env(safe-area-inset-left));
  }
  /* La franja, el texto y el pie arrancan en la misma columna: 34rem de contenido + 16px por lado. */
  .franja-dentro, .pie-dentro { width: 100%; max-width: 34rem; margin: 0 auto; }
  main { width: min(100%, calc(34rem + 32px)); margin: 0 auto; }
  .franja-dentro { display: flex; align-items: center; gap: 12px; }
  .logo { display: block; height: 44px; width: auto; }
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
    ${LOGO}
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

  if (paginasPublicasAbiertas(env) && esPublica(request)) return next()

  return respuestaDeMantenimiento(request)
}
