import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import { CodigoQr } from '../componentes/tarjetas/CodigoQr'
import { emitirTarjeta, type TarjetaEmitida } from '../funciones/interno/servicio'
import * as T from '../funciones/interno/textos'
import { enlaceQr, enlaceVisible, fechaDeTarjeta } from '../funciones/tarjetas/servicio'
import { useTitulo } from '../marco/useTitulo'

/**
 * JQ-13 · CU-19. Emitir una tarjeta (API-59) y mostrar su código de activación UNA sola vez.
 *
 * El código vive SOLO en el estado de este componente: no va a localStorage, sessionStorage, la
 * URL ni el historial. Al salir de la pantalla o recargar, se pierde, y eso es a propósito: la API
 * guarda solo su hash y no lo vuelve a dar. Por eso la pantalla lo dice antes de que se pierda
 * (el aviso, la confirmación de «Ya lo guardé» y la advertencia del navegador al cerrar).
 */
export function EmitirTarjetas() {
  useTitulo(T.TITULO_EMITIR)
  const [emitida, setEmitida] = useState<TarjetaEmitida | null>(null)
  const [emitiendo, setEmitiendo] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Mientras hay un código en pantalla, el navegador avisa antes de cerrar o recargar.
  useEffect(() => {
    if (!emitida) return
    const avisar = (e: BeforeUnloadEvent) => { e.preventDefault() }
    window.addEventListener('beforeunload', avisar)
    return () => window.removeEventListener('beforeunload', avisar)
  }, [emitida])

  async function emitir() {
    setEmitiendo(true)
    setError(null)
    try {
      setEmitida(await emitirTarjeta())
    } catch (e) {
      setError(e instanceof ErrorApi ? e.paraMostrar : 'No se pudo completar la operación.')
    } finally {
      setEmitiendo(false)
    }
  }

  function listo() {
    if (window.confirm(T.CONFIRMAR_LISTO)) setEmitida(null)
  }

  return (
    <div className="contenedor ficha-pagina emitir-tarjetas">
      <h1 className="pagina-titulo">{T.TITULO_EMITIR}</h1>
      <p className="bloque-texto no-imprimir"><Link className="enlace" to="/interno">{T.VOLVER_AL_PANEL}</Link></p>
      <p className="bloque-texto no-imprimir">{T.INTRO_EMITIR}</p>
      {error && <p className="aviso aviso--malo" role="alert">{error}</p>}

      {!emitida && (
        <button type="button" className="boton boton--tinta" disabled={emitiendo} onClick={() => void emitir()}>
          {emitiendo ? T.EMITIENDO : T.BOTON_EMITIR}
        </button>
      )}

      {emitida && (
        <section className="bloque" aria-label={T.ETIQUETA_CODIGO}>
          <p className="aviso aviso--malo no-imprimir" role="alert">{T.AVISO_UNA_VEZ}</p>
          <div className="etiqueta-imprimible">
            <dl>
              <dt>{T.ETIQUETA_ENLACE}</dt>
              <dd>{enlaceVisible(emitida.segmento)}</dd>
              <dt>{T.ETIQUETA_CODIGO}</dt>
              <dd className="codigo-activacion">{emitida.codigo}</dd>
              <dt>{T.ETIQUETA_VENCE}</dt>
              <dd>{fechaDeTarjeta(emitida.venceEn)}</dd>
            </dl>
            <CodigoQr texto={enlaceQr(emitida.segmento)} alt={T.qrDeTarjeta(enlaceVisible(emitida.segmento))} />
          </div>
          <div className="editor-acciones no-imprimir">
            <button type="button" className="boton boton--tinta" onClick={() => window.print()}>{T.BOTON_IMPRIMIR}</button>
            <button type="button" className="boton" onClick={listo}>{T.BOTON_LISTO}</button>
          </div>
        </section>
      )}
    </div>
  )
}
