import type { FichaPublica } from '../funciones/busqueda/servicio'

/**
 * Los mismos textos que la Function de /prestadores/:slug (functions/_comun/seo.js) pone del lado del
 * servidor. Se repiten aqui porque al navegar DENTRO de la app (de la busqueda a una ficha) el servidor no
 * interviene, y la descripcion tiene que ser la misma en los dos caminos. Una prueba de paridad
 * (`src/pruebas-functions/paridad.prueba.js`) falla si se separan.
 */

function limpiar(valor: string | null | undefined): string {
  return (valor ?? '')
    // eslint-disable-next-line no-control-regex
    .replace(/[\u0000-\u001f\u007f\u2028\u2029]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
}

/** Recorta en el limite de una palabra y agrega «…». */
export function recortar(texto: string, maximo: number): string {
  const t = limpiar(texto)
  if (t.length <= maximo) return t
  const corte = t.slice(0, maximo - 1)
  const ultimoEspacio = corte.lastIndexOf(' ')
  return `${(ultimoEspacio > maximo * 0.6 ? corte.slice(0, ultimoEspacio) : corte).replace(/[\s,.;:]+$/, '')}…`
}

export function descripcionDeFicha(ficha: Pick<FichaPublica, 'nombrePublico' | 'descripcion' | 'rubro' | 'zonas'>): string {
  const nombre = limpiar(ficha.nombrePublico)
  const oficio = limpiar(ficha.rubro?.nombre)
  const zonas = ficha.zonas.map((z) => ({ nombre: limpiar(z.nombre), principal: z.esPrincipal })).filter((z) => z.nombre !== '')
  const principal = (zonas.find((z) => z.principal) ?? zonas[0])?.nombre ?? ''
  const descripcion = limpiar(ficha.descripcion)

  const resumen = oficio && principal ? `${oficio} en ${principal}` : oficio || (principal ? `En ${principal}` : '')
  const base = descripcion
    ? (resumen ? `${resumen}. ${descripcion}` : descripcion)
    : (resumen
        ? `${nombre}: ${resumen}. Mira su ficha y contáctala directo en ZASS!.`
        : `Mira la ficha de ${nombre} en ZASS!.`)
  return recortar(base, 160)
}
