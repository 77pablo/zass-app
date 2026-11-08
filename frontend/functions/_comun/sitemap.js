/**
 * Logica del sitemap. Lee los slugs publicados de la API (GET /api/prestadores/mapa, paginado por cursor)
 * y arma el XML. Separado de las Functions para poder probarlo sin Cloudflare.
 */
import { CABECERAS_DE_SEGURIDAD, escaparHtml, origenDelSitio } from './seo.js'

/** Paginas fijas del sitio. Las privadas (/mi-ficha, /entrar...) NO van: tambien estan en robots.txt. */
export const RUTAS_ESTATICAS = ['/', '/buscar', '/comunas', '/oficios', '/profesionales', '/terminos', '/privacidad']

/** Limite del protocolo sitemap: 50 000 URLs por archivo. */
export const MAXIMO_URLS = 50_000

const POR_PAGINA = 1000
/**
 * Cloudflare limita las subpeticiones por invocacion (50 en el plan gratis). Dejamos margen: 45 paginas de 1000
 * son 45 000 fichas. Con plan de pago se sube con la variable SITEMAP_MAX_PAGINAS.
 */
const PAGINAS_POR_OMISION = 45
const ESPERA_MAXIMA_MS = 8000

export class ErrorDeApi extends Error {}

/** Todas las fichas visibles, recorriendo el cursor. Lanza ErrorDeApi si algo falla: un sitemap a medias es peor que ninguno. */
export async function listarFichas(env, request) {
  if (!env.API_ORIGEN) throw new ErrorDeApi('Falta API_ORIGEN')
  const maxPaginas = Number.parseInt(env.SITEMAP_MAX_PAGINAS, 10) > 0 ? Number.parseInt(env.SITEMAP_MAX_PAGINAS, 10) : PAGINAS_POR_OMISION

  const fichas = []
  let cursor = null
  for (let pagina = 0; pagina < maxPaginas; pagina++) {
    const url = new URL('/api/prestadores/mapa', env.API_ORIGEN)
    url.searchParams.set('limite', String(POR_PAGINA))
    if (cursor) url.searchParams.set('cursor', cursor)

    const cabeceras = { accept: 'application/json' }
    const ip = request.headers.get('CF-Connecting-IP')
    if (ip) cabeceras['X-Forwarded-For'] = ip

    let cuerpo
    try {
      const r = await fetch(url, { headers: cabeceras, signal: AbortSignal.timeout(ESPERA_MAXIMA_MS) })
      if (!r.ok) throw new ErrorDeApi(`La API respondio ${r.status}`)
      cuerpo = await r.json()
    } catch (e) {
      throw e instanceof ErrorDeApi ? e : new ErrorDeApi('La API no respondio')
    }

    if (!cuerpo || !Array.isArray(cuerpo.prestadores)) throw new ErrorDeApi('Respuesta sin forma de mapa')
    for (const p of cuerpo.prestadores) {
      if (p && typeof p.slug === 'string' && p.slug !== '') fichas.push({ slug: p.slug, publicadoEn: p.publicadoEn ?? null })
    }
    cursor = typeof cuerpo.cursorSiguiente === 'string' && cuerpo.cursorSiguiente !== '' ? cuerpo.cursorSiguiente : null
    if (cursor === null) return fichas
  }
  // Se acabo el presupuesto de paginas con cursor pendiente: se entrega lo recorrido (ver DESPLIEGUE).
  return fichas
}

/** AAAA-MM-DD si la fecha es valida; si no, nada (lastmod es opcional, una fecha mala invalida el archivo). */
export function fechaCorta(valor) {
  if (typeof valor !== 'string') return null
  const t = Date.parse(valor)
  return Number.isNaN(t) ? null : new Date(t).toISOString().slice(0, 10)
}

/** Las URLs, en orden: primero las fijas, luego las fichas. */
export function entradas(origen, fichas) {
  return [
    ...RUTAS_ESTATICAS.map((ruta) => ({ loc: `${origen}${ruta}`, lastmod: null })),
    ...fichas.map((f) => ({ loc: `${origen}/prestadores/${encodeURIComponent(f.slug)}`, lastmod: fechaCorta(f.publicadoEn) })),
  ]
}

/** Parte la lista en archivos de a lo mas `maximo` URLs. */
export function partir(lista, maximo = MAXIMO_URLS) {
  const trozos = []
  for (let i = 0; i < lista.length; i += maximo) trozos.push(lista.slice(i, i + maximo))
  return trozos.length > 0 ? trozos : [[]]
}

export function xmlDeUrls(lista) {
  const filas = lista.map((e) =>
    `  <url><loc>${escaparHtml(e.loc)}</loc>${e.lastmod ? `<lastmod>${e.lastmod}</lastmod>` : ''}</url>`)
  return `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${filas.join('\n')}\n</urlset>\n`
}

export function xmlDeIndice(origen, cantidad) {
  const filas = Array.from({ length: cantidad }, (_, i) =>
    `  <sitemap><loc>${escaparHtml(`${origen}/sitemaps/${i + 1}.xml`)}</loc></sitemap>`)
  return `<?xml version="1.0" encoding="UTF-8"?>\n<sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${filas.join('\n')}\n</sitemapindex>\n`
}

const CABECERAS_XML = {
  'Content-Type': 'application/xml; charset=utf-8',
  'X-Content-Type-Options': CABECERAS_DE_SEGURIDAD['X-Content-Type-Options'],
  'Strict-Transport-Security': CABECERAS_DE_SEGURIDAD['Strict-Transport-Security'],
}

export function respuestaXml(xml, request) {
  return new Response(request.method === 'HEAD' ? null : xml, {
    status: 200,
    headers: { ...CABECERAS_XML, 'Cache-Control': 'public, max-age=0, s-maxage=3600' },
  })
}

/** La API no esta: 503 con Retry-After es lo que un rastreador entiende como «vuelve luego»; nunca 500. */
export function respuestaNoDisponible() {
  return new Response('Sitemap no disponible por ahora.\n', {
    status: 503,
    headers: { ...CABECERAS_XML, 'Content-Type': 'text/plain; charset=utf-8', 'Retry-After': '300', 'Cache-Control': 'no-store' },
  })
}

/** Lo comun de las dos Functions: leer, partir y devolver el archivo pedido (0 = el de /sitemap.xml). */
export async function servirSitemap({ request, env }, numero, maximo = MAXIMO_URLS) {
  if (request.method !== 'GET' && request.method !== 'HEAD') return new Response(null, { status: 405, headers: { Allow: 'GET, HEAD' } })

  let fichas
  try {
    fichas = await listarFichas(env, request)
  } catch {
    return respuestaNoDisponible()
  }

  const origen = origenDelSitio(env)
  const trozos = partir(entradas(origen, fichas), maximo)

  if (numero === 0) {
    return respuestaXml(trozos.length === 1 ? xmlDeUrls(trozos[0]) : xmlDeIndice(origen, trozos.length), request)
  }
  // /sitemaps/N.xml solo existe cuando hay indice; sin el, o fuera de rango, es un 404 comun.
  if (trozos.length === 1 || numero < 1 || numero > trozos.length) {
    return new Response('No existe.\n', { status: 404, headers: { ...CABECERAS_XML, 'Content-Type': 'text/plain; charset=utf-8', 'Cache-Control': 'public, max-age=0, s-maxage=60' } })
  }
  return respuestaXml(xmlDeUrls(trozos[numero - 1]), request)
}
