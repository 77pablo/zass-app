// @vitest-environment node
//
// El estilo de ZASS!, vigilado. Empezo como la guia v3 (aprobada por Pablo el 17-nov-2026) y el
// 18-nov-2026 paso a «estilo iPhone»: estructura de iOS (fondo agrupado #F2F2F7, celdas blancas,
// barras de vidrio arriba y abajo, letra del sistema) con la marca solo de acento. Las reglas de
// contraste de la v3 siguen. Lo que se rompe sin que nadie lo vea: un degradado que vuelve, una
// variable que ya no existe (el color cae al del navegador sin avisar), una pareja de colores que
// pierde contraste, o Montserrat que se vuelve a colar. axe lo mide en un navegador; esto lo mide
// en cada `npm run prueba`.
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const SRC = resolve(import.meta.dirname, '..')
const css = readFileSync(resolve(SRC, 'index.css'), 'utf8')
const legal = readFileSync(resolve(SRC, 'legal.css'), 'utf8')
const html = readFileSync(resolve(SRC, '..', 'index.html'), 'utf8')
const sinComentarios = (t) => t.replace(/\/\*[\s\S]*?\*\//g, '')
const sinComentariosHtml = (t) => t.replace(/<!--[\s\S]*?-->/g, '')

/** Las variables definidas en :root, con su valor. */
const tokens = Object.fromEntries(
  [...sinComentarios(css).matchAll(/^\s*(--[a-z0-9-]+):\s*([^;]+);/gm)].map((m) => [m[1], m[2].trim()]),
)
const hex = (nombre) => {
  const v = tokens[nombre]
  expect(v, `${nombre} no esta definida en :root`).toBeDefined()
  expect(v, `${nombre} tiene que ser un hex para medir su contraste`).toMatch(/^#[0-9a-f]{6}$/i)
  return v
}

/** Las reglas de la hoja (selector y cuerpo). Las de dentro de un @media salen con su selector. */
const reglas = [...sinComentarios(css).matchAll(/([^{}]+)\{([^{}]*)\}/g)]
  .map(([, selector, cuerpo]) => ({ selector: selector.trim(), cuerpo }))
/** El primer bloque @media cuya condicion es `condicion`, entero (con sus reglas adentro). */
function bloqueMedia(condicion) {
  const t = sinComentarios(css)
  const inicio = t.indexOf(`@media (${condicion})`)
  if (inicio === -1) return null
  let nivel = 0
  for (let i = t.indexOf('{', inicio); i < t.length; i++) {
    if (t[i] === '{') nivel++
    else if (t[i] === '}' && --nivel === 0) return t.slice(inicio, i + 1)
  }
  return null
}

// WCAG 2.x: luminancia relativa y razon de contraste.
function luminancia(h) {
  const [r, g, b] = [1, 3, 5].map((i) => parseInt(h.slice(i, i + 2), 16) / 255)
    .map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4))
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}
function contraste(a, b) {
  const [x, y] = [luminancia(a), luminancia(b)].sort((p, q) => q - p)
  return (x + 0.05) / (y + 0.05)
}

describe('estilo iPhone: la paleta', () => {
  it('trae los colores de la marca y el fondo agrupado de iOS, con su valor exacto', () => {
    expect(hex('--blanco').toLowerCase()).toBe('#ffffff')
    expect(hex('--fondo-agrupado').toLowerCase()).toBe('#f2f2f7')
    expect(hex('--pizarra').toLowerCase()).toBe('#111827')
    expect(hex('--celeste').toLowerCase()).toBe('#00a6ff')
    expect(hex('--ambar').toLowerCase()).toBe('#ffb800')
    expect(hex('--esmeralda').toLowerCase()).toBe('#10b981')
    // La excepcion que decidio Pablo: rojo oscuro, solo para errores.
    expect(hex('--malo').toLowerCase()).toBe('#b42318')
  })

  it('el fondo de toda pantalla es el gris agrupado de iOS, no el blanco', () => {
    const body = reglas.find((r) => r.selector === 'body')
    expect(body, 'no esta la regla body').toBeDefined()
    expect(body.cuerpo).toMatch(/background:\s*var\(--fondo-agrupado\)/)
  })

  // Cada pareja que la hoja de estilos usa para TEXTO. 4,5:1 es el minimo de WCAG AA para texto normal.
  it.each([
    ['--pizarra', '--blanco'],
    ['--pizarra', '--fondo-agrupado'],
    ['--pizarra', '--relleno'],
    ['--suave', '--blanco'],
    ['--suave', '--gris'],
    ['--suave', '--fondo-agrupado'],
    ['--suave', '--relleno'],
    ['--pizarra', '--celeste'],
    ['--pizarra', '--ambar'],
    ['--pizarra', '--esmeralda'],
    ['--malo', '--blanco'],
    ['--malo', '--fondo-agrupado'],
    ['--esmeralda-texto', '--blanco'],
    ['--esmeralda-texto', '--fondo-agrupado'],
    ['--esmeralda-texto', '--esmeralda-suave'],
  ])('texto %s sobre %s llega a 4,5:1', (texto, fondo) => {
    expect(contraste(hex(texto), hex(fondo))).toBeGreaterThanOrEqual(4.5)
  })

  // WCAG 1.4.11: el borde de un campo tiene que verse (3:1) para saber donde se escribe, sobre la
  // celda blanca y sobre el fondo gris (el campo de busqueda va directo sobre el gris).
  it.each([['--blanco'], ['--fondo-agrupado']])('el borde de los campos se ve sobre %s (3:1)', (fondo) => {
    expect(contraste(hex('--borde-campo'), hex(fondo))).toBeGreaterThanOrEqual(3)
  })

  // Lo que la guia prohibe porque NO se lee: si alguien lo usa, que sea a sabiendas.
  it('el ambar y el blanco sobre celeste no se leen, y por eso no se usan como texto', () => {
    expect(contraste(hex('--blanco'), hex('--celeste'))).toBeLessThan(3)
    expect(contraste(hex('--ambar'), hex('--blanco'))).toBeLessThan(3)
    // Quien pinta letra con ambar o celeste. Permitido solo el celeste en iconos (no son texto).
    // El ambar ya no tiene excepcion: el pie y las cabeceras oscuras, donde se leia, no existen.
    const COLOR = /(?<![-\w])color:\s*var\(--(ambar|celeste)\)/
    const malas = reglas
      .filter(({ cuerpo }) => COLOR.test(cuerpo))
      .map(({ selector, cuerpo }) => ({ selector, color: cuerpo.match(COLOR)[1] }))
      .filter(({ selector, color }) => color === 'ambar'
        || !selector.split(',').every((s) => /\.icono\s*$/.test(s.trim())))
    expect(malas).toEqual([])
  })

  // «Solo de acento»: el celeste lleno, solo en la pestaña puesta de la barra de abajo (y siempre
  // con el icono en pizarra encima); el ambar lleno, en botones principales, precios, cupos y el «!».
  it('el celeste lleno solo pinta la pestaña puesta, con letra pizarra', () => {
    const conCeleste = reglas.filter(({ cuerpo }) => /background:\s*var\(--celeste\)/.test(cuerpo))
    expect(conCeleste.map((r) => r.selector)).toEqual(['.pestana.active .pestana-icono'])
    expect(conCeleste[0].cuerpo).toMatch(/(?<![-\w])color:\s*var\(--pizarra\)/)
  })
})

describe('estilo iPhone: la letra', () => {
  it('usa la letra del sistema: SF en el iPhone, la del telefono en Android', () => {
    const raiz = reglas.find((r) => r.selector === ':root')
    expect(raiz.cuerpo).toMatch(/font-family:\s*-apple-system,\s*BlinkMacSystemFont,\s*"SF Pro Text",\s*system-ui,\s*sans-serif/)
  })

  it('no carga ni nombra Montserrat (ni ninguna fuente descargada)', () => {
    expect(sinComentarios(css)).not.toMatch(/Montserrat/i)
    expect(sinComentarios(legal)).not.toMatch(/Montserrat/i)
    expect(sinComentariosHtml(html)).not.toMatch(/Montserrat|fonts\.googleapis|fonts\.gstatic/i)
    expect(sinComentarios(css)).not.toMatch(/@font-face/)
  })

  // Apple: el tracking depende del tamaño. Los titulos grandes lo llevan negativo; un valor
  // positivo (o ninguno) los deja con las letras separadas.
  it.each([['.titular'], ['.pagina-titulo']])('el titulo grande %s lleva tracking negativo', (selector) => {
    const r = reglas.find((x) => x.selector === selector)
    expect(r, `no esta la regla ${selector}`).toBeDefined()
    const m = r.cuerpo.match(/letter-spacing:\s*(-?[\d.]+)em/)
    expect(m, `${selector} sin letter-spacing en em`).not.toBeNull()
    expect(Number(m[1])).toBeLessThan(0)
  })
})

describe('estilo iPhone: sin lo «tecnologico»', () => {
  it.each([['index.css', css], ['legal.css', legal]])('%s no tiene degradados', (_n, hoja) => {
    expect(sinComentarios(hoja)).not.toMatch(/gradient\(/)
  })

  it('no carga ni usa la fuente de codigo', () => {
    expect(sinComentarios(css)).not.toMatch(/JetBrains|var\(--mono\)/)
    expect(html).not.toMatch(/JetBrains/)
  })

  // El vidrio va en las dos barras (arriba y abajo) y en ningun otro lado: sobre una tarjeta o un
  // texto largo, un material translucido se come la lectura.
  it('el vidrio solo va en la barra de arriba y en la de abajo', () => {
    const conVidrio = reglas
      // `[^;\s]` y no `\s*(?!none)`: con este ultimo, el `\s*` cede el espacio y « none» pasaba por vidrio.
      .filter(({ cuerpo }) => /(?<!-webkit-)backdrop-filter:\s*(?!none\b)[^;\s]/.test(cuerpo))
      .map((r) => r.selector)
    expect(conVidrio).toEqual(['.cabecera-fila', '.barra-inferior'])
  })

  it('con «reducir transparencia», las dos barras quedan solidas y sin desenfoque', () => {
    const bloque = bloqueMedia('prefers-reduced-transparency: reduce')
    expect(bloque, 'falta @media (prefers-reduced-transparency: reduce)').not.toBeNull()
    expect(bloque).toMatch(/\.cabecera-fila,\s*\.barra-inferior\s*\{[^}]*background:\s*var\(--blanco\)[^}]*(?<!-webkit-)backdrop-filter:\s*none/)
  })

  it.each([['index.css', css], ['legal.css', legal]])('%s solo usa variables que existen', (_n, hoja) => {
    const usadas = [...sinComentarios(hoja).matchAll(/var\((--[a-z0-9-]+)/g)].map((m) => m[1])
    // Las que se ponen desde el codigo (style={{ '--d': ... }}) y llevan valor por omision en el var().
    const delCodigo = new Set(['--d', '--orden', '--x', '--y', '--mx', '--my'])
    const faltan = [...new Set(usadas)].filter((v) => !(v in tokens) && !delCodigo.has(v))
    expect(faltan).toEqual([])
  })
})
