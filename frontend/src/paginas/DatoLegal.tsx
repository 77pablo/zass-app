import { estaPendiente } from './legal'

/**
 * Un dato de `legal.ts`. Si falta llenarlo se ve resaltado (para que nadie lo publique sin notarlo); si es
 * el correo y ya esta lleno, se vuelve un enlace.
 */
export function DatoLegal({ valor, comoCorreo = false }: { valor: string; comoCorreo?: boolean }) {
  if (estaPendiente(valor)) return <span className="legal-pendiente">{valor}</span>
  if (comoCorreo) return <a href={`mailto:${valor}`}>{valor}</a>
  return <>{valor}</>
}
