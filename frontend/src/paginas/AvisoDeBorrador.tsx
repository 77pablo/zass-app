import { BORRADOR_LEGAL } from './legal'

/**
 * El aviso de arriba de las paginas legales. `activo` existe para poder probar los dos estados sin
 * tocar la constante; en el sitio siempre se usa el valor por omision.
 */
export function AvisoDeBorrador({ activo = BORRADOR_LEGAL }: { activo?: boolean }) {
  if (!activo) return null
  return (
    <p className="legal-borrador" role="note">
      <strong>Borrador: pendiente de revisión legal.</strong> Este texto describe lo que ZASS! hace hoy,
      pero todavía no lo ha revisado un abogado y puede cambiar.
    </p>
  )
}
