import { useEffect } from 'react'

export interface Meta {
  /**
   * Titulo para el recuadro que se ve al compartir el enlace (Open Graph y Twitter). Sin esto se
   * quedaba el de `index.html`, asi que en WhatsApp el titulo y el texto no hablaban de lo mismo.
   * Va completo, con la marca: no se le agrega nada, al contrario que en `useTitulo`.
   */
  titulo?: string
  /** Descripcion para buscadores y para el texto que se ve al compartir el enlace. */
  descripcion?: string
  /** Fuera del indice de buscadores: paginas privadas, formularios, errores. */
  noindex?: boolean
}

/**
 * Pone una etiqueta <meta> y devuelve como deshacerlo. Se crea si no existe (las rutas de la app no
 * recargan index.html) y al salir de la pagina se restaura lo que habia: asi un `noindex` de /entrar no se
 * queda pegado cuando alguien navega a la portada dentro de la misma pestana.
 */
function fijar(selector: string, atributos: Record<string, string>, contenido: string): () => void {
  let el = document.head.querySelector<HTMLMetaElement>(selector)
  const existia = el !== null
  const anterior = el?.getAttribute('content') ?? null

  if (!el) {
    el = document.createElement('meta')
    for (const [k, v] of Object.entries(atributos)) el.setAttribute(k, v)
    document.head.appendChild(el)
  }
  el.setAttribute('content', contenido)

  const puesto = el
  return () => {
    if (!existia) puesto.remove()
    else if (anterior !== null) puesto.setAttribute('content', anterior)
  }
}

/**
 * Las etiquetas por pagina que `useTitulo` no cubre: descripcion (tambien la de Open Graph y Twitter) y
 * `noindex`. Hecho para llamarse como `useTitulo`, al principio del componente de la pagina.
 *
 * Lo que NO hace: cambiar el HTML que ve Google sin ejecutar JavaScript. Para las fichas eso lo hace la
 * Function de /prestadores/:slug; esto cubre a los navegadores y a Google cuando si renderiza.
 */
export function useMeta({ descripcion, noindex = false }: Meta): void {
  useEffect(() => {
    const deshacer: (() => void)[] = []
    if (descripcion !== undefined) {
      deshacer.push(fijar('meta[name="description"]', { name: 'description' }, descripcion))
      deshacer.push(fijar('meta[property="og:description"]', { property: 'og:description' }, descripcion))
      deshacer.push(fijar('meta[name="twitter:description"]', { name: 'twitter:description' }, descripcion))
    }
    // Sin `noindex` no se toca la etiqueta: si el servidor ya puso una (ficha inexistente), no es nuestra para quitarla.
    if (noindex) deshacer.push(fijar('meta[name="robots"]', { name: 'robots' }, 'noindex, nofollow'))
    // Se deshace en orden inverso: lo ultimo que se puso es lo primero que se quita.
    return () => { for (const d of deshacer.reverse()) d() }
  }, [descripcion, noindex])
}
