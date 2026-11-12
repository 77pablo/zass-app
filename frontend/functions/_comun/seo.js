/**
 * Piezas compartidas por las Pages Functions de SEO. Los archivos de esta carpeta no exportan
 * `onRequest*`, asi que Cloudflare no los convierte en rutas: son solo codigo importado.
 *
 * Todo lo que llega de la API (nombres, descripciones, zonas) es contenido escrito por usuarios. Aqui se trata
 * SIEMPRE como hostil: se escapa para HTML, se escapa distinto para JSON-LD, y jamas se pega en una
 * cadena de reemplazo (`String.replace` interpreta `$&`, `$1`... en el texto de reemplazo).
 */

/** Origen publico del sitio, para canonical y Open Graph. Una variable, no una constante, por si cambia el dominio. */
export function origenDelSitio(env) {
  const crudo = typeof env?.SITIO_ORIGEN === 'string' ? env.SITIO_ORIGEN.trim().replace(/\/+$/, '') : ''
  return /^https:\/\/[a-z0-9.-]+(:\d+)?$/i.test(crudo) ? crudo : 'https://zass.cl'
}

/**
 * Las mismas cabeceras de seguridad que `public/_headers` para `/*`. Existen aqui porque Cloudflare Pages
 * NO aplica `_headers` a las respuestas de las Functions, solo a los archivos estaticos: sin esto, la ficha
 * (que sale de una Function) iria sin CSP ni HSTS. Una prueba compara este objeto con `_headers`.
 */
export const CABECERAS_DE_SEGURIDAD = {
  'Content-Security-Policy':
    "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src https://fonts.gstatic.com; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'",
  'X-Frame-Options': 'DENY',
  'X-Content-Type-Options': 'nosniff',
  'Referrer-Policy': 'strict-origin-when-cross-origin',
  'Permissions-Policy': 'camera=(), microphone=(), payment=(), geolocation=(self)',
  'Strict-Transport-Security': 'max-age=31536000; includeSubDomains',
}

/** Escapa texto para ir DENTRO de un elemento o de un atributo entre comillas dobles. */
export function escaparHtml(valor) {
  return String(valor ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
    .replace(/`/g, '&#96;')
}

/**
 * JSON para ir dentro de `<script type="application/ld+json">`. JSON.stringify no basta: un `</script>` dentro
 * de un texto cerraria el bloque y lo que siga seria HTML. Se escapan `<`, `>` y `&` como \uXXXX (siguen
 * siendo JSON valido y significan lo mismo) y los separadores de linea de JS.
 */
export function jsonParaScript(objeto) {
  return JSON.stringify(objeto)
    .replace(/</g, '\\u003c')
    .replace(/>/g, '\\u003e')
    .replace(/&/g, '\\u0026')
    .replace(/\u2028/g, '\\u2028')
    .replace(/\u2029/g, '\\u2029')
}

/** Una sola linea, sin caracteres de control ni espacios repetidos. */
export function limpiarTexto(valor) {
  return String(valor ?? '')
    // eslint-disable-next-line no-control-regex
    .replace(/[\u0000-\u001f\u007f\u2028\u2029]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
}

/** Recorta en el limite de una palabra y agrega «…». Misma logica que `src/marco/seo.ts` (hay una prueba de paridad). */
export function recortar(texto, maximo) {
  const t = limpiarTexto(texto)
  if (t.length <= maximo) return t
  const corte = t.slice(0, maximo - 1)
  const ultimoEspacio = corte.lastIndexOf(' ')
  return `${(ultimoEspacio > maximo * 0.6 ? corte.slice(0, ultimoEspacio) : corte).replace(/[\s,.;:]+$/, '')}…`
}

const NOMBRE = (v) => (typeof v === 'string' ? limpiarTexto(v) : '')

/**
 * De la ficha publica (API-05) saca SOLO lo que se publica en las etiquetas: nombre, oficio, zonas y
 * descripcion. Es una lista blanca: aunque la API un dia mandara de mas (contacto, correo, coordenadas),
 * nada de eso llega al HTML. Devuelve null si lo que llego no tiene forma de ficha.
 */
export function datosDeFicha(ficha) {
  if (ficha === null || typeof ficha !== 'object') return null
  const nombre = NOMBRE(ficha.nombrePublico)
  if (nombre === '') return null

  const oficio = NOMBRE(ficha.rubro?.nombre)
  const zonas = Array.isArray(ficha.zonas)
    ? ficha.zonas
        .filter((z) => z && typeof z === 'object')
        .map((z) => ({ nombre: NOMBRE(z.nombre), principal: z.esPrincipal === true }))
        .filter((z) => z.nombre !== '')
    : []
  const principal = (zonas.find((z) => z.principal) ?? zonas[0])?.nombre ?? ''
  const descripcion = NOMBRE(ficha.descripcion)

  // RN-S6-10. La ficha de quien dejo de pagar SIGUE respondiendo 200 —se abre por la tarjeta
  // NFC, por un enlace guardado o por un enlace compartido—, pero no entra al indice: con
  // RN-S3-09 ya no esta en la busqueda ni en el mapa del sitio, y esta era la otra puerta.
  //
  // `=== false` y no `!== true` a proposito: si la API no mandara el campo (una version vieja
  // durante un despliegue), se indexa como siempre. Al reves, un campo ausente sacaria del
  // indice a TODAS las fichas de golpe, que es un daño mucho mayor y mucho mas lento de
  // deshacer que indexar unas horas de mas a un impago.
  const indexable = ficha.suscripcionVigente !== false

  return { nombre, oficio, zonas: zonas.map((z) => z.nombre), principal, descripcion, indexable }
}

/** «Corretaje de propiedades en Centro»; sin zona, solo el oficio; sin nada, cadena vacia. */
export function resumenDeOficio(d) {
  if (d.oficio && d.principal) return `${d.oficio} en ${d.principal}`
  return d.oficio || (d.principal ? `En ${d.principal}` : '')
}

export function tituloDeFicha(d) {
  const resumen = resumenDeOficio(d)
  return recortar(resumen ? `${d.nombre} · ${resumen} · ZASS!` : `${d.nombre} · ZASS!`, 70)
}

export function descripcionDeFicha(d) {
  const resumen = resumenDeOficio(d)
  const base = d.descripcion
    ? (resumen ? `${resumen}. ${d.descripcion}` : d.descripcion)
    : (resumen
        ? `${d.nombre}: ${resumen}. Mira su ficha y contáctala directo en ZASS!.`
        : `Mira la ficha de ${d.nombre} en ZASS!.`)
  return recortar(base, 160)
}

/**
 * El bloque de &lt;head&gt; de una ficha. Cada valor pasa por escaparHtml o jsonParaScript.
 *
 * Con `d.indexable` en falso (RN-S6-10) la ficha conserva sus etiquetas —quien la comparta por
 * WhatsApp sigue viendo de quien es— pero se va con `noindex, nofollow` y SIN los datos
 * estructurados: anunciar un `ProfessionalService` que ahora mismo no se puede ni contactar ni
 * agendar seria decirle a Google algo que la pagina ya no cumple.
 */
export function metaDeFicha(d, slug, origen) {
  const url = `${origen}/prestadores/${encodeURIComponent(slug)}`
  const titulo = tituloDeFicha(d)
  const descripcion = descripcionDeFicha(d)
  const imagen = `${origen}/og-zass.png`

  const ld = {
    '@context': 'https://schema.org',
    '@type': 'ProfessionalService',
    name: d.nombre,
    url,
    description: descripcion,
    ...(d.oficio ? { serviceType: d.oficio } : {}),
    ...(d.zonas.length > 0 ? { areaServed: d.zonas.map((z) => ({ '@type': 'Place', name: z })) } : {}),
  }

  return [
    ...(d.indexable ? [] : [`<meta name="robots" content="noindex, nofollow" />`]),
    `<title>${escaparHtml(titulo)}</title>`,
    `<meta name="description" content="${escaparHtml(descripcion)}" />`,
    `<link rel="canonical" href="${escaparHtml(url)}" />`,
    `<meta property="og:type" content="profile" />`,
    `<meta property="og:site_name" content="ZASS!" />`,
    `<meta property="og:locale" content="es_CL" />`,
    `<meta property="og:title" content="${escaparHtml(titulo)}" />`,
    `<meta property="og:description" content="${escaparHtml(descripcion)}" />`,
    `<meta property="og:url" content="${escaparHtml(url)}" />`,
    `<meta property="og:image" content="${escaparHtml(imagen)}" />`,
    `<meta property="og:image:width" content="1200" />`,
    `<meta property="og:image:height" content="630" />`,
    `<meta name="twitter:card" content="summary_large_image" />`,
    `<meta name="twitter:title" content="${escaparHtml(titulo)}" />`,
    `<meta name="twitter:description" content="${escaparHtml(descripcion)}" />`,
    `<meta name="twitter:image" content="${escaparHtml(imagen)}" />`,
    ...(d.indexable ? [`<script type="application/ld+json">${jsonParaScript(ld)}</script>`] : []),
  ].join('\n    ')
}

/** Para una ficha que no existe: la app sigue, pero fuera del indice. */
export const META_NOINDEX =
  '<title>Ficha no encontrada · ZASS!</title>\n    <meta name="robots" content="noindex, nofollow" />'

/** Para un slug con forma imposible: ni se le pregunta a la API; la app decide, pero sin indexar. */
export const META_SIN_INDEXAR = '<meta name="robots" content="noindex, nofollow" />'

export const MARCA_INICIO = '<!--seo:inicio-->'
export const MARCA_FIN = '<!--seo:fin-->'

/**
 * Reemplaza el bloque entre marcas de index.html por `bloque`. Sin replace(): un nombre que contenga `$&` o
 * `$'` no se interpretaria. Sin marcas (index.html distinto del esperado) devuelve el HTML tal cual: una
 * ficha sin meta es mejor que una pagina rota.
 */
export function inyectar(html, bloque) {
  const i = html.indexOf(MARCA_INICIO)
  const f = html.indexOf(MARCA_FIN)
  if (i === -1 || f === -1 || f < i) return html
  return html.slice(0, i) + bloque + html.slice(f + MARCA_FIN.length)
}

/** Cabeceras de una respuesta HTML de estas Functions. */
export function cabecerasHtml(cacheControl) {
  return {
    ...CABECERAS_DE_SEGURIDAD,
    'Content-Type': 'text/html; charset=utf-8',
    'Cache-Control': cacheControl,
  }
}
