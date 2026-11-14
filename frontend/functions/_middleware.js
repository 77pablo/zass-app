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

function paginaDeMantenimiento() {
  return `<!doctype html>
<html lang="es-CL">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex, nofollow">
<title>${TEXTOS.titulo}</title>
<style>
  :root { --amarillo: #F6C81E; --tinta: #1A1A1A; --crema: #FDF8E7; }
  * { box-sizing: border-box; }
  body {
    margin: 0; min-height: 100vh; display: grid; place-items: center;
    padding: 24px; background: var(--crema); color: var(--tinta);
    font-family: Montserrat, "Segoe UI", system-ui, -apple-system, sans-serif;
    line-height: 1.6;
  }
  main { max-width: 34rem; text-align: center; }
  .marca {
    font-size: clamp(2.5rem, 12vw, 4rem); font-weight: 800; letter-spacing: -0.02em;
    margin: 0 0 8px;
  }
  .marca span { color: var(--amarillo); -webkit-text-stroke: 1px var(--tinta); }
  h1 { font-size: clamp(1.25rem, 5vw, 1.6rem); margin: 0 0 16px; }
  p { margin: 0 0 16px; }
  .destacado {
    background: #fff; border: 2px solid var(--tinta); border-radius: 14px;
    padding: 16px 20px; margin-top: 24px; text-align: left;
  }
  a { color: var(--tinta); font-weight: 700; }
  footer { margin-top: 28px; font-size: 0.9rem; opacity: 0.75; }
</style>
</head>
<body>
<main>
  <p class="marca">ZAS<span>S!</span></p>
  <h1>${TEXTOS.encabezado}</h1>
  <p>${TEXTOS.cuerpo}</p>
  <div class="destacado">
    <p>${TEXTOS.prestadores}</p>
    <p style="margin:0"><a href="mailto:${TEXTOS.correo}">${TEXTOS.correo}</a></p>
  </div>
  <footer>${TEXTOS.pie}</footer>
</main>
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

export async function onRequest(contexto) {
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
