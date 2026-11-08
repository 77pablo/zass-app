// @vitest-environment node
//
// La Function de /prestadores/:slug, probada con Request/Response de verdad y un fetch simulado (la API).
// NO prueba Cloudflare: prueba la logica. El index.html es el REAL del repositorio, para que quitar las
// marcas `seo:inicio/seo:fin` rompa una prueba y no el SEO en silencio.
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { JSDOM } from 'jsdom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { onRequest } from '../../functions/prestadores/[slug].js'
import { CABECERAS_DE_SEGURIDAD } from '../../functions/_comun/seo.js'

const RAIZ = resolve(import.meta.dirname, '..', '..')
const INDEX_HTML = readFileSync(resolve(RAIZ, 'index.html'), 'utf8')

const FICHA = {
  id: '11111111-1111-1111-1111-111111111111',
  slug: 'marcela-soto',
  nombrePublico: 'Marcela Soto',
  descripcion: 'Corredora de propiedades en Temuco.',
  urlFoto: null,
  rubro: { id: 1, codigo: 'corretaje', nombre: 'Corretaje de propiedades', icono: null },
  zonas: [
    { nombre: 'Amanecer', sector: 'poniente', esPrincipal: false },
    { nombre: 'Centro', sector: 'centro', esPrincipal: true },
  ],
  servicios: [{ nombre: 'Tasación', duracionMinutos: 45, precioReferencia: 50000 }],
  credenciales: [],
  promedioCalificacion: null,
  totalResenas: 0,
  contacto: { telefono: '+56 9 1234 5678', whatsapp: '912345678', correo: 'contacto@soto.cl', sitioWeb: 'https://soto.cl' },
  publicadoEn: '2026-11-01T12:00:00Z',
}

let llamadasApi
let respuestaApi

function json(cuerpo, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

function entorno(extra = {}) {
  return {
    API_ORIGEN: 'https://api.ejemplo.test',
    ASSETS: { fetch: vi.fn(async (peticion) => {
      const url = new URL(typeof peticion === 'string' || peticion instanceof URL ? peticion : peticion.url)
      return url.pathname === '/' ? new Response(INDEX_HTML, { headers: { 'content-type': 'text/html' } }) : new Response('estatico', { status: 200 })
    }) },
    ...extra,
  }
}

async function pedir(slug, { env = entorno(), metodo = 'GET', cabeceras = {} } = {}) {
  const request = new Request(`https://zass.cl/prestadores/${slug}`, { method: metodo, headers: cabeceras })
  return onRequest({ request, env, params: { slug } })
}

function dom(html) {
  return new JSDOM(html).window.document
}

beforeEach(() => {
  llamadasApi = []
  respuestaApi = () => json(FICHA)
  vi.stubGlobal('fetch', vi.fn(async (url, opciones) => {
    llamadasApi.push({ url: String(url), opciones })
    return respuestaApi()
  }))
})
afterEach(() => vi.unstubAllGlobals())

describe('index.html real', () => {
  it('trae las marcas del bloque que la Function reemplaza, una sola vez y dentro del head', () => {
    expect(INDEX_HTML.split('<!--seo:inicio-->')).toHaveLength(2)
    expect(INDEX_HTML.split('<!--seo:fin-->')).toHaveLength(2)
    const cabeza = INDEX_HTML.slice(INDEX_HTML.indexOf('<head>'), INDEX_HTML.indexOf('</head>'))
    expect(cabeza).toContain('<!--seo:inicio-->')
    expect(cabeza).toContain('<!--seo:fin-->')
  })
})

describe('ficha existente', () => {
  it('devuelve la app con titulo, descripcion, canonical, Open Graph, Twitter y JSON-LD de la ficha', async () => {
    const r = await pedir('marcela-soto')
    expect(r.status).toBe(200)
    const d = dom(await r.text())

    expect(d.title).toBe('Marcela Soto · Corretaje de propiedades en Centro · ZASS!')
    expect(d.querySelector('meta[name="description"]').content).toBe(
      'Corretaje de propiedades en Centro. Corredora de propiedades en Temuco.')
    expect(d.querySelector('link[rel="canonical"]').href).toBe('https://zass.cl/prestadores/marcela-soto')
    expect(d.querySelector('meta[property="og:title"]').content).toBe(d.title)
    expect(d.querySelector('meta[property="og:url"]').content).toBe('https://zass.cl/prestadores/marcela-soto')
    expect(d.querySelector('meta[property="og:image"]').content).toBe('https://zass.cl/og-zass.png')
    expect(d.querySelector('meta[name="twitter:card"]').content).toBe('summary_large_image')
    expect(d.querySelector('meta[name="robots"]')).toBeNull()

    const ld = JSON.parse(d.querySelector('script[type="application/ld+json"]').textContent)
    expect(ld).toMatchObject({ '@type': 'ProfessionalService', name: 'Marcela Soto', serviceType: 'Corretaje de propiedades' })
    expect(ld.areaServed.map((z) => z.name)).toEqual(['Amanecer', 'Centro'])
  })

  it('reemplaza las etiquetas por defecto, no las duplica', async () => {
    const d = dom(await (await pedir('marcela-soto')).text())
    expect(d.querySelectorAll('title')).toHaveLength(1)
    expect(d.querySelectorAll('meta[name="description"]')).toHaveLength(1)
    expect(d.querySelectorAll('meta[property="og:title"]')).toHaveLength(1)
    expect(d.querySelectorAll('meta[property="og:image"]')).toHaveLength(1)
    expect(d.querySelector('#root')).not.toBeNull()
    expect(d.querySelector('script[type="module"]')).not.toBeNull()
  })

  it('se cachea en el borde unos minutos y lleva las cabeceras de seguridad (las Functions no reciben _headers)', async () => {
    const r = await pedir('marcela-soto')
    expect(r.headers.get('cache-control')).toMatch(/s-maxage=300/)
    expect(r.headers.get('content-type')).toMatch(/text\/html/)
    for (const [k, v] of Object.entries(CABECERAS_DE_SEGURIDAD)) expect(r.headers.get(k)).toBe(v)
  })

  it('pregunta a la API por el slug, con la IP real del visitante', async () => {
    await pedir('marcela-soto', { cabeceras: { 'CF-Connecting-IP': '203.0.113.9' } })
    expect(llamadasApi).toHaveLength(1)
    expect(llamadasApi[0].url).toBe('https://api.ejemplo.test/api/prestadores/marcela-soto')
    expect(llamadasApi[0].opciones.headers['X-Forwarded-For']).toBe('203.0.113.9')
  })

  it('usa SITIO_ORIGEN para el canonical, y lo ignora si no es https', async () => {
    const bueno = dom(await (await pedir('marcela-soto', { env: entorno({ SITIO_ORIGEN: 'https://www.zass.cl/' }) })).text())
    expect(bueno.querySelector('link[rel="canonical"]').href).toBe('https://www.zass.cl/prestadores/marcela-soto')
    const malo = dom(await (await pedir('marcela-soto', { env: entorno({ SITIO_ORIGEN: 'javascript:alert(1)' }) })).text())
    expect(malo.querySelector('link[rel="canonical"]').href).toBe('https://zass.cl/prestadores/marcela-soto')
  })

  it('un HEAD no lleva cuerpo; un POST pasa a los archivos estaticos', async () => {
    const head = await pedir('marcela-soto', { metodo: 'HEAD' })
    expect(head.status).toBe(200)
    expect(await head.text()).toBe('')
    const env = entorno()
    const post = await pedir('marcela-soto', { metodo: 'POST', env })
    expect(await post.text()).toBe('estatico')
    expect(llamadasApi).toHaveLength(1) // solo el del HEAD
  })
})

// ---------------------------------------------------------------- contenido hostil

const HOSTILES = [
  '"><script>alert(1)</script>',
  '</title><script>alert(2)</script>',
  '</script><img src=x onerror=alert(3)>',
  "' onmouseover='alert(4)",
  '" onmouseover="alert(7)" x="',
  'Tom & Jerry &lt;b&gt; &amp; &#60;',
  '<!--seo:fin--><script>alert(5)</script>',
  '`${alert(6)}`',
  'Línea 1\nLínea 2\r\n<b>negrita</b>',
  'separadores \u2028 y \u2029 de linea',
]

describe('contenido hostil de usuarios', () => {
  for (const hostil of HOSTILES) {
    it(`no rompe el HTML ni inyecta nada: ${JSON.stringify(hostil).slice(0, 40)}`, async () => {
      respuestaApi = () => json({
        ...FICHA, nombrePublico: `Ana ${hostil}`, descripcion: hostil,
        rubro: { ...FICHA.rubro, nombre: hostil },
        zonas: [{ nombre: hostil, sector: 'x', esPrincipal: true }],
      })
      const html = await (await pedir('marcela-soto')).text()
      const d = dom(html)

      // Ningun elemento nuevo: solo el script de la app y el bloque de datos JSON-LD.
      const scripts = [...d.querySelectorAll('script')].map((s) => s.type)
      expect(scripts.sort()).toEqual(['application/ld+json', 'module'])
      expect(d.querySelector('img')).toBeNull()
      expect(d.querySelector('b')).toBeNull()
      // Ningun atributo de evento en ningun elemento.
      const conEvento = [...d.querySelectorAll('*')].filter((el) => [...el.attributes].some((a) => /^on/i.test(a.name)))
      expect(conEvento).toHaveLength(0)
      // El texto original sobrevive como TEXTO: el titulo y el JSON-LD lo devuelven tal cual (sin saltos de linea).
      // eslint-disable-next-line no-control-regex
      const limpio = (s) => s.replace(/[\u0000-\u001f\u007f\u2028\u2029]+/g, ' ').replace(/\s+/g, ' ').trim()
      const ld = JSON.parse(d.querySelector('script[type="application/ld+json"]').textContent)
      expect(ld.name).toBe(limpio(`Ana ${hostil}`))
      expect(ld.areaServed[0].name).toBe(limpio(hostil))
      // El bloque JSON-LD no contiene un cierre de script ni un `<` sin escapar.
      expect(d.querySelector('script[type="application/ld+json"]').textContent).not.toContain('<')
      // La ficha sigue siendo una sola: un meta de mas seria una inyeccion de atributos.
      expect(d.querySelectorAll('meta[name="description"]')).toHaveLength(1)
      expect(d.querySelectorAll('meta[property="og:title"]')).toHaveLength(1)
      expect(d.querySelectorAll('link[rel="canonical"]')).toHaveLength(1)
      expect(d.querySelector('meta[name="robots"]')).toBeNull()
    })
  }

  it('los patrones especiales de String.replace ($&, $\', $`, $1) se quedan como texto', async () => {
    respuestaApi = () => json({ ...FICHA, nombrePublico: "Ana $& $' $` $1 $$", descripcion: 'x' })
    const d = dom(await (await pedir('marcela-soto')).text())
    expect(d.title).toContain("Ana $& $' $` $1 $$")
    // Y el resto de la pagina sigue ahi: un `$\`` mal interpretado pegaria el HTML anterior dentro del titulo.
    expect(d.title.length).toBeLessThan(120)
    expect(d.querySelector('#root')).not.toBeNull()
  })

  it('el ampersand se escapa: «&lt;» escrito por un usuario se lee como texto, no como «<»', async () => {
    respuestaApi = () => json({ ...FICHA, nombrePublico: 'Tom &lt;b&gt; & Jerry' })
    const html = await (await pedir('marcela-soto')).text()
    expect(html).toContain('<title>Tom &amp;lt;b&amp;gt; &amp; Jerry')
    expect(dom(html).title).toContain('Tom &lt;b&gt; & Jerry')
  })

  it('una descripcion enorme se recorta', async () => {
    respuestaApi = () => json({ ...FICHA, descripcion: 'palabra '.repeat(2000) })
    const d = dom(await (await pedir('marcela-soto')).text())
    expect(d.querySelector('meta[name="description"]').content.length).toBeLessThanOrEqual(160)
  })

  it('el slug de la URL no se cuela sin codificar en el canonical ni en la consulta a la API', async () => {
    // Un slug con forma imposible ni siquiera se le pregunta a la API.
    const r = await pedir('a"><script>x</script>')
    expect(llamadasApi).toHaveLength(0)
    const d = dom(await r.text())
    expect(d.querySelector('meta[name="robots"]').content).toBe('noindex, nofollow')
    expect(d.querySelectorAll('script')).toHaveLength(1)
    expect(d.querySelector('link[rel="canonical"]')).toBeNull()
  })
})

// ---------------------------------------------------------------- datos privados

describe('datos privados', () => {
  it('aunque la API mandara de mas, nada de contacto ni ubicacion llega al HTML', async () => {
    respuestaApi = () => json({
      ...FICHA,
      direccion: 'Calle Secreta 123', latitud: -38.7359, longitud: -72.5904,
      usuarioId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      correoPrivado: 'privado@zass.local', telefonoPrivado: '+56 9 0000 0000',
    })
    const html = await (await pedir('marcela-soto')).text()
    for (const prohibido of [
      'Calle Secreta', '38.7359', '72.5904', 'aaaaaaaa-bbbb', 'privado@zass.local', '0000 0000',
      // El contacto publico tampoco se publica en las etiquetas: las etiquetas no son para eso.
      'contacto@soto.cl', '912345678', '1234 5678', 'soto.cl',
    ]) expect(html, prohibido).not.toContain(prohibido)
  })
})

// ---------------------------------------------------------------- 404 y fallos

describe('ficha inexistente o despublicada', () => {
  it('responde la app con noindex y estado 404', async () => {
    respuestaApi = () => new Response(null, { status: 404 })
    const r = await pedir('no-existe')
    expect(r.status).toBe(404)
    const d = dom(await r.text())
    expect(d.querySelector('meta[name="robots"]').content).toBe('noindex, nofollow')
    expect(d.querySelector('#root')).not.toBeNull()
    expect(d.querySelector('script[type="application/ld+json"]')).toBeNull()
    expect(d.querySelector('link[rel="canonical"]')).toBeNull()
    expect(r.headers.get('cache-control')).toMatch(/s-maxage=60/)
    for (const [k, v] of Object.entries(CABECERAS_DE_SEGURIDAD)) expect(r.headers.get(k)).toBe(v)
  })
})

describe('la API falla', () => {
  async function esAppNormalSinMeta(r) {
    // Nunca un 500 hacia Google: 200, la app, las etiquetas por defecto, y cache cortisima.
    expect(r.status).toBe(200)
    const d = dom(await r.text())
    expect(d.title).toBe('ZASS! · Conecta con un toque')
    expect(d.querySelector('script[type="application/ld+json"]')).toBeNull()
    expect(d.querySelector('meta[name="robots"]')).toBeNull()
    expect(d.querySelector('#root')).not.toBeNull()
    expect(r.headers.get('cache-control')).toMatch(/s-maxage=10(?!\d)/)
    expect(r.headers.get('cache-control')).not.toMatch(/s-maxage=300/)
  }

  it.each([500, 502, 503, 429, 400])('con un %i de la API', async (estado) => {
    respuestaApi = () => new Response('error', { status: estado })
    await esAppNormalSinMeta(await pedir('marcela-soto'))
  })

  it('un 500 de la API con cuerpo que parece una ficha NO se usa para las etiquetas', async () => {
    respuestaApi = () => json(FICHA, 500)
    await esAppNormalSinMeta(await pedir('marcela-soto'))
  })

  it('si el fetch revienta (red caida, tiempo agotado)', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('network') }))
    await esAppNormalSinMeta(await pedir('marcela-soto'))
  })

  it('si responde algo que no es JSON', async () => {
    respuestaApi = () => new Response('<html>gateway</html>', { status: 200 })
    await esAppNormalSinMeta(await pedir('marcela-soto'))
  })

  it.each([[null], [[]], ['texto'], [{}], [{ nombrePublico: '   ' }], [{ nombrePublico: 42 }]])(
    'si responde 200 con algo que no es una ficha (%j)', async (cuerpo) => {
      respuestaApi = () => json(cuerpo)
      await esAppNormalSinMeta(await pedir('marcela-soto'))
    })

  it('sin API_ORIGEN configurada', async () => {
    await esAppNormalSinMeta(await pedir('marcela-soto', { env: entorno({ API_ORIGEN: undefined }) }))
    expect(llamadasApi).toHaveLength(0)
  })

  it('si no hay index.html, delega en los archivos estaticos en vez de romper', async () => {
    const env = entorno()
    env.ASSETS.fetch = vi.fn(async () => new Response('no hay', { status: 404 }))
    const r = await pedir('marcela-soto', { env })
    expect(r.status).toBe(404)
    expect(llamadasApi).toHaveLength(0)
  })
})

describe('cabeceras de seguridad', () => {
  it('son exactamente las de public/_headers para /*', () => {
    const texto = readFileSync(resolve(RAIZ, 'public', '_headers'), 'utf8')
    const bloque = texto.split(/\r?\n\r?\n/)[0].split(/\r?\n/).slice(1)
    const delArchivo = Object.fromEntries(bloque.map((l) => {
      const i = l.indexOf(':')
      return [l.slice(0, i).trim().toLowerCase(), l.slice(i + 1).trim()]
    }))
    const delCodigo = Object.fromEntries(Object.entries(CABECERAS_DE_SEGURIDAD).map(([k, v]) => [k.toLowerCase(), v]))
    expect(delCodigo).toEqual(delArchivo)
  })

  it('la CSP no permite scripts inline: el JSON-LD es un bloque de datos y no se ejecuta', () => {
    expect(CABECERAS_DE_SEGURIDAD['Content-Security-Policy']).toMatch(/script-src 'self'(;|$)/)
    expect(CABECERAS_DE_SEGURIDAD['Content-Security-Policy']).not.toMatch(/script-src[^;]*unsafe-inline/)
  })
})
