import { useRef, type CSSProperties, type ReactNode } from 'react'
import { useVisto } from './useVisto'

/**
 * Hace que lo de adentro entre con una animacion al aparecer en pantalla. `orden` escalona a los hermanos:
 * en una rejilla, cada tarjeta entra un instante despues de la anterior.
 */
export function Aparece({
  children, orden = 0, className = '',
}: { children: ReactNode; orden?: number; className?: string }) {
  const ref = useRef<HTMLDivElement>(null)
  const visto = useVisto(ref)
  return (
    <div
      ref={ref} className={`aparece${visto ? ' visto' : ''}${className ? ` ${className}` : ''}`}
      style={{ '--orden': Math.min(orden, 8) } as CSSProperties}
    >
      {children}
    </div>
  )
}
