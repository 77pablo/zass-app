import { useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import { fechaLarga } from '../funciones/perfil/estadoSuscripcion'
import type { Suscripcion } from '../funciones/perfil/servicio'
import { inscribirTarjeta } from '../funciones/planes/servicio'

/*
 * El cupo fundador se toma SIN tarjeta y la tarjeta se pide al terminar el mes gratis
 * (decisión de Pablo, 20-nov-2026). Esto es el recordatorio en Mi ficha: mientras corre el
 * mes gratis y no hay tarjeta, se dice hasta cuándo y qué pasa si no la inscribe.
 *
 * ES-1 (20-nov-2026): el mismo aviso sirve cuando la tarjeta SÍ está inscrita pero el cobro
 * fue rechazado. Hay gracia hasta el vencimiento, con un reintento por día; aquí se dice
 * cuántos días quedan y se ofrece cambiar la tarjeta. Cambiarla usa el mismo endpoint
 * (API-76): al fundador que ya tiene su cupo solo le actualiza el medio de pago.
 *
 * TEXTOS PROPUESTOS, pendientes de aprobación.
 */

/** Solo a quien es fundador, sigue en su mes gratis y todavía no inscribe la tarjeta. */
export function debePedirTarjeta(s: Suscripcion | null): boolean {
  return s !== null && s.esFundador === true && s.tarjetaInscrita !== true && s.vigente
}

/** ES-1. Fundador vigente cuyo cobro fue rechazado: el servidor manda los días de gracia. */
export function estaEnGracia(s: Suscripcion | null): boolean {
  return s !== null && s.esFundador === true && s.vigente
    && typeof s.diasDeGracia === 'number' && s.diasDeGracia > 0
}

function plazo(dias: number): string {
  return dias <= 1 ? 'mañana' : `en ${dias} días`
}

export function TarjetaDelFundador({ suscripcion }: { suscripcion: Suscripcion }) {
  const navegar = useNavigate()
  const [abriendo, setAbriendo] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // CC-04: una clave por visita, la misma en cada reintento.
  const clave = useRef(crypto.randomUUID()).current

  const hasta = fechaLarga(suscripcion.vigenteHasta)
  const enGracia = estaEnGracia(suscripcion)
  const fallo = enGracia
    ? 'No pudimos abrir el cambio de tarjeta. Vuelve a intentarlo.'
    : 'No pudimos abrir la inscripción de la tarjeta. Vuelve a intentarlo.'

  async function inscribir() {
    if (abriendo) return
    setAbriendo(true)
    setError(null)
    try {
      const intento = await inscribirTarjeta(clave)
      if (intento.urlParaPagar === null) {
        setError(fallo)
        return
      }
      if (intento.urlParaPagar.startsWith('/')) navegar(intento.urlParaPagar)
      else window.location.assign(intento.urlParaPagar)
    } catch (e) {
      setError(e instanceof ErrorApi ? e.mensaje : fallo)
    } finally {
      setAbriendo(false)
    }
  }

  return (
    <section className="aviso aviso--atencion" aria-labelledby="tarjeta-fundador">
      {enGracia ? (
        <>
          <h2 id="tarjeta-fundador">No pudimos cobrar tu suscripción</h2>
          <p>
            Tu tarjeta fue rechazada. Tu suscripción vence{' '}
            <strong>{plazo(suscripcion.diasDeGracia as number)}</strong>
            {hasta && <> (el {hasta})</>} y lo volveremos a intentar cada día hasta entonces.
            Si no se puede cobrar antes, pierdes tu cupo de fundador y tu precio congelado, y tu
            ficha deja de aparecer en las búsquedas.
          </p>
        </>
      ) : (
        <>
          <h2 id="tarjeta-fundador">Inscribe tu tarjeta para mantener tu cupo</h2>
          <p>
            Tu mes gratis de fundador termina {hasta ? <>el <strong>{hasta}</strong></> : 'pronto'}.
            Inscribe tu tarjeta antes de esa fecha: hoy no se te cobra nada. Si el mes termina sin
            tarjeta, tu cupo queda libre para otra persona y tu ficha deja de aparecer en las
            búsquedas.
          </p>
        </>
      )}
      <button
        type="button"
        className="boton boton--amarillo boton--lleno"
        onClick={() => void inscribir()}
        disabled={abriendo}
      >
        {abriendo ? 'Abriendo…' : enGracia ? 'Actualizar mi tarjeta' : 'Inscribir mi tarjeta'}
      </button>
      {error !== null && <p className="aviso aviso--error" role="alert">{error}</p>}
    </section>
  )
}
