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
 * TEXTOS PROPUESTOS, pendientes de aprobación de Pablo.
 */

/** Solo a quien es fundador, sigue en su mes gratis y todavía no inscribe la tarjeta. */
export function debePedirTarjeta(s: Suscripcion | null): boolean {
  return s !== null && s.esFundador === true && s.tarjetaInscrita !== true && s.vigente
}

export function TarjetaDelFundador({ suscripcion }: { suscripcion: Suscripcion }) {
  const navegar = useNavigate()
  const [abriendo, setAbriendo] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // CC-04: una clave por visita, la misma en cada reintento.
  const clave = useRef(crypto.randomUUID()).current

  const hasta = fechaLarga(suscripcion.vigenteHasta)

  async function inscribir() {
    if (abriendo) return
    setAbriendo(true)
    setError(null)
    try {
      const intento = await inscribirTarjeta(clave)
      if (intento.urlParaPagar === null) {
        setError('No pudimos abrir la inscripción de la tarjeta. Vuelve a intentarlo.')
        return
      }
      if (intento.urlParaPagar.startsWith('/')) navegar(intento.urlParaPagar)
      else window.location.assign(intento.urlParaPagar)
    } catch (e) {
      setError(e instanceof ErrorApi
        ? e.mensaje
        : 'No pudimos abrir la inscripción de la tarjeta. Vuelve a intentarlo.')
    } finally {
      setAbriendo(false)
    }
  }

  return (
    <section className="aviso aviso--atencion" aria-labelledby="tarjeta-fundador">
      <h2 id="tarjeta-fundador">Inscribe tu tarjeta para mantener tu cupo</h2>
      <p>
        Tu mes gratis de fundador termina {hasta ? <>el <strong>{hasta}</strong></> : 'pronto'}.
        Inscribe tu tarjeta antes de esa fecha: hoy no se te cobra nada. Si el mes termina sin
        tarjeta, tu cupo queda libre para otra persona y tu ficha deja de aparecer en las
        búsquedas.
      </p>
      <button
        type="button"
        className="boton boton--amarillo boton--lleno"
        onClick={() => void inscribir()}
        disabled={abriendo}
      >
        {abriendo ? 'Abriendo…' : 'Inscribir mi tarjeta'}
      </button>
      {error !== null && <p className="aviso aviso--error" role="alert">{error}</p>}
    </section>
  )
}
