import { Icono } from '../../marco/Icono'
import { CALIFICACION_MAXIMA, CALIFICACION_MINIMA } from '../../funciones/resenas/servicio'

const VALORES = Array.from(
  { length: CALIFICACION_MAXIMA - CALIFICACION_MINIMA + 1 },
  (_, i) => CALIFICACION_MINIMA + i,
)

/** Lo que significa cada nota. Sale junto al selector: "4" no le dice nada a nadie. */
const QUE_SIGNIFICA: Record<number, string> = {
  1: 'Muy malo',
  2: 'Malo',
  3: 'Aceptable',
  4: 'Bueno',
  5: 'Excelente',
}

export const significado = (valor: number): string => QUE_SIGNIFICA[valor] ?? ''

const nombre = (valor: number) =>
  valor === 1 ? '1 estrella' : `${valor} estrellas`

/**
 * La calificacion de una reseña ya escrita. Los dibujos son decorativos: lo que lee un lector
 * de pantalla es el texto, nunca la cuenta de iconos.
 */
export function Estrellas({ valor }: { valor: number }) {
  return (
    <span className="estrellas" aria-label={`${valor} de ${CALIFICACION_MAXIMA}`}>
      {VALORES.map((v) => (
        <Icono key={v} nombre="estrella" className={v <= valor ? 'estrella estrella--llena' : 'estrella'} />
      ))}
    </span>
  )
}

/**
 * CU-16 paso 3. Un grupo de radios de verdad, no botones: asi se recorre con las flechas, se
 * envia con el formulario y un lector de pantalla dice cuantas opciones hay y cual esta marcada.
 */
export function SelectorDeEstrellas({ valor, alElegir, idEtiqueta }: {
  valor: number | null
  alElegir: (valor: number) => void
  idEtiqueta: string
}) {
  return (
    <div className="estrellas-elegir" role="radiogroup" aria-labelledby={idEtiqueta}>
      {VALORES.map((v) => (
        <label key={v} className={v === valor ? 'estrella-opcion estrella-opcion--activa' : 'estrella-opcion'}>
          <input
            type="radio" name="calificacion" value={v} checked={v === valor}
            onChange={() => alElegir(v)}
          />
          <Icono nombre="estrella" className={valor !== null && v <= valor ? 'estrella estrella--llena' : 'estrella'} />
          <span className="estrella-nombre">{nombre(v)}</span>
        </label>
      ))}
    </div>
  )
}
