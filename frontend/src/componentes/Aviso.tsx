import type { ReactNode } from 'react'

/**
 * Mensaje de error del formulario. role="alert" hace que el lector de pantalla
 * lo lea en cuanto aparece, sin que la persona tenga que ir a buscarlo.
 */
export function Aviso({ children, tono = 'malo' }: { children: ReactNode; tono?: 'malo' | 'bueno' }) {
  return <p className={`aviso aviso--${tono}`} role="alert">{children}</p>
}
