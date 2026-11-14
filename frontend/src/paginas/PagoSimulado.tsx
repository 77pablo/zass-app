import { useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { enviar } from '../api/cliente'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'

/*
 * La pasarela de PRUEBA. Existe para poder recorrer el camino completo —tomar el cupo,
 * inscribir el medio de pago, volver y aparecer en la búsqueda— sin que haya dinero de por
 * medio ni una pasarela de verdad decidida.
 *
 * Dice con todas las letras que es de prueba, y en grande: una pantalla de pago que se
 * parece a la de un banco y no lo es, es la clase de cosa que alguien llega a confundir.
 *
 * El aviso se manda a API-70 con los nombres de campo que espera el adaptador simulado
 * (`intento`, `monto`, `estado`, `referencia`). No se inventan aquí: están en
 * `PasarelaSimulada.cs` y si cambian allá, esta pantalla deja de servir.
 *
 * TEXTOS PROPUESTOS, pendientes de aprobación de Pablo.
 */

/** El monto de una inscripción: cero. No se cobra nada, se autoriza el cobro de después. */
const MONTO_DE_LA_INSCRIPCION = '0'

export function PagoSimulado() {
  useTitulo('Pasarela de prueba')
  useMeta({ noindex: true })

  const [parametros] = useSearchParams()
  const navegar = useNavigate()
  const intento = parametros.get('intento') ?? parametros.get('ref') ?? ''

  const [enviando, setEnviando] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function avisar(aprobado: boolean) {
    if (enviando) return
    setEnviando(true)
    setError(null)
    try {
      await enviar('/api/pagos/aviso', {
        intento,
        monto: MONTO_DE_LA_INSCRIPCION,
        estado: aprobado ? 'aprobado' : 'rechazado',
        referencia: `prueba-${intento.slice(0, 8)}`,
      })
      navegar('/planes/listo')
    } catch {
      setError('La pasarela de prueba no pudo avisar. Vuelve a intentarlo.')
    } finally {
      setEnviando(false)
    }
  }

  if (intento === '') {
    return (
      <article className="pagina pagina--angosta">
        <h1>Pasarela de prueba</h1>
        <p className="aviso aviso--error" role="alert">
          Falta el identificador del cobro. Vuelve a la página de planes y empieza de nuevo.
        </p>
      </article>
    )
  }

  return (
    <article className="pagina pagina--angosta">
      <p className="aviso aviso--atencion" role="note">
        <strong>Esta es una pasarela de PRUEBA.</strong> No es un banco, no se cobra nada y no
        pide ningún dato de tarjeta. Existe para probar el camino completo antes de que ZASS!
        cobre de verdad.
      </p>

      <h1>Inscribir el medio de pago</h1>
      <p className="bloque-texto">
        Al inscribirlo, tu cupo de fundador queda confirmado y tu primer mes empieza a correr.
        Hoy no se te cobra nada.
      </p>

      <div className="pago-acciones">
        <button
          type="button"
          className="boton boton--amarillo boton--lleno"
          onClick={() => void avisar(true)}
          disabled={enviando}
        >
          {enviando ? 'Avisando…' : 'Inscribir mi medio de pago'}
        </button>
        <button
          type="button"
          className="boton"
          onClick={() => void avisar(false)}
          disabled={enviando}
        >
          Simular un rechazo
        </button>
      </div>

      {error !== null && <p className="aviso aviso--error" role="alert">{error}</p>}
    </article>
  )
}
