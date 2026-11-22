import { fotoChica } from './fotos'
import { iniciales } from './texto'

/** Lo que mide cada tamaño en pantalla, en px CSS. Lo mismo que `.monograma--*` en index.css. */
const LADO = { p: 34, m: 44, g: 96 } as const

/**
 * La cara de una persona: su foto si subió una (JQ-9) y, si no, sus iniciales en el gris de los
 * contactos sin foto de iOS. Decorativa en los dos casos: el nombre siempre va escrito al lado,
 * así que un lector de pantalla no necesita oírlo dos veces.
 *
 * Con `srcSet` + `sizes` el navegador elige el archivo justo: para un círculo de 44 px en un
 * celular de densidad 2x baja el de 96 (unos pocos KB), nunca el de 288. `width` y `height` fijos
 * reservan el lugar antes de que llegue la imagen, y la página no salta al cargarla.
 */
export function Monograma({
  nombre, tamano = 'm', foto,
}: { nombre: string; tamano?: 'p' | 'm' | 'g'; foto?: string | null }) {
  if (foto) {
    const chica = fotoChica(foto)
    const lado = LADO[tamano]
    return (
      <img
        className={`monograma monograma--${tamano} monograma--foto`}
        src={foto}
        srcSet={chica ? `${chica} 96w, ${foto} 288w` : undefined}
        sizes={chica ? `${lado}px` : undefined}
        width={lado}
        height={lado}
        alt=""
        aria-hidden="true"
        // La ficha muestra la foto grande arriba de todo: esa se pide al tiro. Las tarjetas de
        // una lista, recién cuando se acercan a la pantalla.
        loading={tamano === 'g' ? 'eager' : 'lazy'}
        decoding="async"
      />
    )
  }
  return (
    <span className={`monograma monograma--${tamano}`} aria-hidden="true">
      {iniciales(nombre)}
    </span>
  )
}
