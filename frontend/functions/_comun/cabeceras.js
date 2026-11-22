/*
 * Las cabeceras de seguridad del sitio, para las respuestas que arma una Function.
 *
 * public/_headers las pone en los archivos estaticos, pero Cloudflare Pages NO aplica ese
 * archivo a las respuestas que genera una Function: la pagina de mantenimiento (503), la
 * ficha de un prestador, el mapa del sitio, la tarjeta /t/... Medido el 17-nov-2026: el 503
 * de https://zass.cl/ salia sin Content-Security-Policy, sin X-Frame-Options, sin nada.
 *
 * Tienen que ser IGUALES al bloque /* de public/_headers: lo vigila cabeceras.prueba.js.
 * Si cambias una, cambia la otra.
 */
export const CABECERAS_DE_SEGURIDAD = {
  'Content-Security-Policy': "default-src 'self'; script-src 'self' https://challenges.cloudflare.com; frame-src https://challenges.cloudflare.com; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src https://fonts.gstatic.com; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'",
  'X-Frame-Options': 'DENY',
  'X-Content-Type-Options': 'nosniff',
  'Referrer-Policy': 'strict-origin-when-cross-origin',
  'Permissions-Policy': 'camera=(), microphone=(), payment=(), geolocation=(self)',
  'Strict-Transport-Security': 'max-age=31536000; includeSubDomains',
}

/**
 * Agrega las que FALTAN y no pisa las que ya trae la respuesta: la API (por el proxy) pone
 * su propia politica, mas estricta, y esa manda.
 */
export function conCabecerasDeSeguridad(respuesta) {
  const faltan = Object.entries(CABECERAS_DE_SEGURIDAD).filter(([nombre]) => !respuesta.headers.has(nombre))
  if (faltan.length === 0) return respuesta
  // Las cabeceras de una respuesta que viene de fetch() o de next() pueden ser inmutables:
  // se copia la respuesta entera (cuerpo, estado y cabeceras) y se agregan a la copia.
  const copia = new Response(respuesta.body, respuesta)
  for (const [nombre, valor] of faltan) copia.headers.set(nombre, valor)
  return copia
}
