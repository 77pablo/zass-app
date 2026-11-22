import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import {
  consultarAuditoria, esGuid, fechaYHora,
  type FiltroDeAuditoria, type FilaDeAuditoria,
} from '../funciones/interno/servicio'
import * as T from '../funciones/interno/textos'
import { useTitulo } from '../marco/useTitulo'

/**
 * JQ-13 · API-61 · TBL-28. El rastro de auditoría por entidad o por actor, del más nuevo al más
 * antiguo, paginado hacia atrás con el cursor que da la API. Se muestra solo lo que la API
 * devuelve: ninguna columna que no esté en su respuesta.
 */
export function Auditoria() {
  useTitulo(T.TITULO_AUDITORIA)
  const [por, setPor] = useState<'entidad' | 'actor'>('entidad')
  const [entidad, setEntidad] = useState('')
  const [entidadId, setEntidadId] = useState('')
  const [actorId, setActorId] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [buscando, setBuscando] = useState(false)
  const [filtro, setFiltro] = useState<FiltroDeAuditoria | null>(null)
  const [filas, setFilas] = useState<FilaDeAuditoria[]>([])
  const [siguiente, setSiguiente] = useState<string | null>(null)

  function armarFiltro(): FiltroDeAuditoria | string {
    if (por === 'entidad') {
      if (entidad.trim() === '') return T.FALTA_ENTIDAD
      if (!esGuid(entidadId)) return T.GUID_MALO
      return { por, entidad: entidad.trim(), entidadId: entidadId.trim() }
    }
    if (!esGuid(actorId)) return T.GUID_MALO
    return { por, actorId: actorId.trim() }
  }

  async function pedir(f: FiltroDeAuditoria, cursor: string | null) {
    setBuscando(true)
    setError(null)
    try {
      const pagina = await consultarAuditoria(f, cursor)
      setFilas((antes) => (cursor ? [...antes, ...pagina.items] : pagina.items))
      setSiguiente(pagina.siguiente)
      setFiltro(f)
    } catch (e) {
      setError(mensajeDeError(e))
    } finally {
      setBuscando(false)
    }
  }

  function buscar(e: FormEvent) {
    e.preventDefault()
    const f = armarFiltro()
    if (typeof f === 'string') { setError(f); return }
    setFilas([])
    setSiguiente(null)
    setFiltro(null)
    void pedir(f, null)
  }

  return (
    <div className="contenedor ficha-pagina mesa-verificacion">
      <h1 className="pagina-titulo">{T.TITULO_AUDITORIA}</h1>
      <p className="bloque-texto"><Link className="enlace" to="/interno">{T.VOLVER_AL_PANEL}</Link></p>

      <form className="bloque editor" onSubmit={buscar} noValidate role="search">
        <fieldset className="campo">
          <legend className="campo-etiqueta">{T.ETIQUETA_BUSCAR_POR}</legend>
          <label className="opcion">
            <input type="radio" name="por" checked={por === 'entidad'} onChange={() => { setPor('entidad'); setError(null) }} />
            {T.POR_ENTIDAD}
          </label>
          <label className="opcion">
            <input type="radio" name="por" checked={por === 'actor'} onChange={() => { setPor('actor'); setError(null) }} />
            {T.POR_ACTOR}
          </label>
        </fieldset>

        {por === 'entidad' ? (
          <>
            <div className="campo">
              <label className="campo-etiqueta" htmlFor="a-entidad">{T.ETIQUETA_ENTIDAD_AUD}</label>
              <input id="a-entidad" className="campo-entrada" value={entidad} onChange={(e) => setEntidad(e.target.value)}
                aria-describedby="a-entidad-ayuda" autoComplete="off" />
              <p id="a-entidad-ayuda" className="letra-chica">{T.AYUDA_ENTIDAD_AUD}</p>
            </div>
            <div className="campo">
              <label className="campo-etiqueta" htmlFor="a-entidad-id">{T.ETIQUETA_ENTIDAD_ID}</label>
              <input id="a-entidad-id" className="campo-entrada" value={entidadId} onChange={(e) => setEntidadId(e.target.value)}
                autoComplete="off" spellCheck={false} />
            </div>
          </>
        ) : (
          <div className="campo">
            <label className="campo-etiqueta" htmlFor="a-actor-id">{T.ETIQUETA_ACTOR_ID}</label>
            <input id="a-actor-id" className="campo-entrada" value={actorId} onChange={(e) => setActorId(e.target.value)}
              autoComplete="off" spellCheck={false} />
          </div>
        )}

        {error && <p className="campo-error" role="alert">{error}</p>}
        <div className="editor-acciones">
          <button type="submit" className="boton boton--tinta" disabled={buscando}>
            {buscando && filas.length === 0 ? T.BUSCANDO : T.BOTON_BUSCAR}
          </button>
        </div>
      </form>

      {filtro && filas.length === 0 && <p className="bloque-texto bloque-vacio" role="status">{T.SIN_RESULTADOS}</p>}
      {filas.length > 0 && (
        <div className="tabla-auditoria">
          <table>
            <caption className="solo-lector">{T.TITULO_AUDITORIA}</caption>
            <thead>
              <tr>
                <th scope="col">{T.COL_CUANDO}</th>
                <th scope="col">{T.COL_ACCION}</th>
                <th scope="col">{T.COL_ENTIDAD}</th>
                <th scope="col">{T.COL_ACTOR}</th>
                <th scope="col">{T.COL_IP}</th>
                <th scope="col">{T.COL_CONTEXTO}</th>
              </tr>
            </thead>
            <tbody>
              {filas.map((f) => (
                <tr key={f.id}>
                  <td>{fechaYHora(f.ocurridoEn)}</td>
                  <td>{f.accion}</td>
                  <td>{f.entidad}{f.entidadId && <><br /><code>{f.entidadId}</code></>}</td>
                  <td>{f.actorId ? <code>{f.actorId}</code> : T.SIN_DATO}</td>
                  <td>{f.ipOrigen ?? T.SIN_DATO}</td>
                  <td>{f.contexto ? <code>{f.contexto}</code> : T.SIN_DATO}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {filtro && filas.length > 0 && (siguiente
        ? (
          <button type="button" className="boton" disabled={buscando} onClick={() => void pedir(filtro, siguiente)}>
            {buscando ? T.BUSCANDO : T.BOTON_MAS_ANTIGUOS}
          </button>
        )
        : <p className="bloque-texto letra-chica">{T.FIN_DEL_RASTRO}</p>)}
    </div>
  )
}

/**
 * Los 400 de API-61 NO vienen en problem+json sino como `{ titulo, detalle }` (EndpointsAuditoria),
 * así que el cliente no encuentra `detail` y daría el mensaje genérico. Se lee `detalle` aquí; la
 * diferencia con api-rest.md queda anotada en el PR.
 */
function mensajeDeError(e: unknown): string {
  if (e instanceof ErrorApi) {
    const detalle = e.datos.detalle
    return typeof detalle === 'string' ? detalle : e.paraMostrar
  }
  return 'No se pudo completar la operación.'
}
