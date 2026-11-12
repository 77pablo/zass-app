import { useState } from 'react'
import { ErrorApi } from '../../api/cliente'
import { Estrellas } from './Estrellas'
import { diaDeChile } from '../../funciones/agenda/servicio'
import {
  ASPECTOS, DETALLE_MAXIMO, MOTIVOS_DE_REPORTE, RESPUESTA_MAXIMA, largo, reportar, responder,
  type MotivoDeReporte, type ResenaPublica,
} from '../../funciones/resenas/servicio'

const ETIQUETA_DE_ASPECTO = new Map(ASPECTOS.map((a) => [a.valor as string, a.etiqueta as string]))

function mensajeDe(e: unknown, porDefecto: string): string {
  return e instanceof ErrorApi ? e.mensaje : porDefecto
}

type Panel = 'reportar' | 'responder' | null

/**
 * Una reseña como la lee cualquiera, con la respuesta del prestador debajo (CU-17) y las dos
 * acciones de CU-18: reportar, y —solo para el dueño del perfil— responder.
 *
 * El comentario se pinta como TEXTO. React escapa lo que se interpola, y aqui no se usa
 * `dangerouslySetInnerHTML` a proposito: RN-S5-04 y CU-16 §7 dicen que el texto se guarda crudo
 * y se escapa al renderizar.
 */
export function TarjetaDeResena({ resena, puedeReportar, puedeResponder, alCambiar }: {
  resena: ResenaPublica
  /** Hay sesion: sin cuenta no se reporta (el servidor lo exige igual). */
  puedeReportar: boolean
  /** Quien mira es el dueño del perfil reseñado. */
  puedeResponder: boolean
  alCambiar: () => void
}) {
  const [panel, setPanel] = useState<Panel>(null)
  const [motivo, setMotivo] = useState<MotivoDeReporte>(MOTIVOS_DE_REPORTE[0].valor)
  const [detalle, setDetalle] = useState('')
  const [texto, setTexto] = useState(resena.respuesta?.texto ?? '')
  const [enviando, setEnviando] = useState(false)
  const [fallo, setFallo] = useState<string | null>(null)
  const [hecho, setHecho] = useState<string | null>(null)

  async function correr(accion: () => Promise<unknown>, aviso: string) {
    if (enviando) return
    setEnviando(true); setFallo(null)
    try {
      await accion()
      setPanel(null); setHecho(aviso)
      alCambiar()
    } catch (e) {
      setFallo(mensajeDe(e, 'No pudimos completar la acción. Intenta de nuevo.'))
    } finally {
      setEnviando(false)
    }
  }

  const respuestaLarga = largo(texto.trim()) > RESPUESTA_MAXIMA

  return (
    <article className="resena">
      <header className="resena-cabecera">
        <Estrellas valor={resena.calificacion} />
        <span className="resena-fecha">
          {diaDeChile(resena.publicadaEn)}
          {resena.editada && <span className="resena-editada"> · editada</span>}
        </span>
      </header>

      {resena.tipo === 'Inasistencia' && (
        <p className="resena-rotulo">Reseña por inasistencia: no habla del servicio.</p>
      )}

      <p className="texto-largo">{resena.comentario}</p>

      {resena.aspectos.length > 0 && (
        <ul className="resena-etiquetas">
          {resena.aspectos.map((a) => (
            <li key={a} className="resena-etiqueta">{ETIQUETA_DE_ASPECTO.get(a) ?? a}</li>
          ))}
        </ul>
      )}

      {resena.respuesta !== null && (
        <aside className="resena-respuesta">
          <h4>Respuesta del prestador</h4>
          <p className="texto-largo">{resena.respuesta.texto}</p>
          <span className="resena-fecha">
            {diaDeChile(resena.respuesta.publicadaEn)}
            {resena.respuesta.editadaEn !== null && <span className="resena-editada"> · editada</span>}
          </span>
        </aside>
      )}

      <div className="resena-acciones">
        {puedeResponder && (
          <button type="button" className="enlace" onClick={() => { setPanel('responder'); setFallo(null) }}>
            {resena.respuesta === null ? 'Responder' : 'Editar mi respuesta'}
          </button>
        )}
        {puedeReportar && (
          <button type="button" className="enlace" onClick={() => { setPanel('reportar'); setFallo(null) }}>
            Reportar
          </button>
        )}
      </div>

      {panel === 'responder' && (
        <div className="resena-panel">
          <label className="campo-nota">
            <span>Tu respuesta, una por reseña. Puedes editarla cuando quieras.</span>
            <textarea rows={4} value={texto} onChange={(e) => setTexto(e.target.value)} />
            <small className={respuestaLarga ? 'resena-cuenta--pasada' : undefined}>
              {largo(texto.trim())} de {RESPUESTA_MAXIMA} caracteres
            </small>
          </label>
          <div className="resena-acciones">
            <button
              type="button" className="boton boton--chico boton--amarillo"
              disabled={enviando || texto.trim() === '' || respuestaLarga}
              onClick={() => { void correr(() => responder(resena.id, texto.trim()), 'Tu respuesta quedó publicada.') }}
            >
              Publicar mi respuesta
            </button>
            <button type="button" className="boton boton--chico" onClick={() => setPanel(null)}>Volver</button>
          </div>
        </div>
      )}

      {panel === 'reportar' && (
        <div className="resena-panel">
          {/* RN-S5-08: con un motivo grave la reseña se oculta en el acto, antes de que ningun
              humano la mire. Se avisa ANTES, o quien reporta ve desaparecer la reseña de la
              lista sin entender que paso. */}
          <p className="letra-chica">
            Si el motivo es grave, la reseña se oculta de inmediato mientras un moderador la revisa.
          </p>
          <fieldset className="resena-motivos">
            <legend>¿Por qué la reportas?</legend>
            {MOTIVOS_DE_REPORTE.map((m) => (
              <label key={m.valor} className="resena-motivo">
                <input
                  type="radio" name={`motivo-${resena.id}`} value={m.valor}
                  checked={motivo === m.valor} onChange={() => setMotivo(m.valor)}
                />
                <span>{m.etiqueta}</span>
              </label>
            ))}
          </fieldset>
          <label className="campo-nota">
            <span>Cuéntanos algo más (opcional)</span>
            <textarea
              rows={3} value={detalle} maxLength={DETALLE_MAXIMO}
              onChange={(e) => setDetalle(e.target.value)}
            />
          </label>
          <div className="resena-acciones">
            <button
              type="button" className="boton boton--chico boton--amarillo" disabled={enviando}
              onClick={() => {
                void correr(
                  () => reportar(resena.id, motivo, detalle),
                  'Recibimos tu reporte. Un moderador lo va a revisar.')
              }}
            >
              Enviar el reporte
            </button>
            <button type="button" className="boton boton--chico" onClick={() => setPanel(null)}>Volver</button>
          </div>
        </div>
      )}

      {fallo !== null && <p className="aviso-linea" role="alert">{fallo}</p>}
      {hecho !== null && panel === null && <p className="aviso-linea aviso--bueno" role="status">{hecho}</p>}
    </article>
  )
}
