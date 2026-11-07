import { iniciales } from './texto'

export function Monograma({
  nombre, tamano = 'm',
}: { nombre: string; tamano?: 'p' | 'm' | 'g' }) {
  return (
    <span className={`monograma monograma--${tamano}`} aria-hidden="true">
      {iniciales(nombre)}
    </span>
  )
}
