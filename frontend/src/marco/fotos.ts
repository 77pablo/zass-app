/**
 * La foto chica que acompaña a la grande (JQ-9): «…/abc.webp» → «…/abc-96.webp». El servidor
 * guarda las dos, cuadradas y en WebP: 288 px (la ficha, hasta densidad 3x) y 96 px (tarjetas y
 * búsqueda). Si la ruta no tiene esa forma, no se inventa una: solo se usa la que vino.
 */
export function fotoChica(url: string): string | null {
  return /\/[0-9a-f]{32}\.webp$/.test(url) ? url.replace(/\.webp$/, '-96.webp') : null
}
