import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import {
  colaDeReportes, MOTIVO_MODERACION_MINIMO, resolverReporte,
  type DecisionDeModeracion, type ReporteEnCola,
} from '../funciones/interno/servicio'
import { fechaYHora } from '../funciones/interno/servicio'
import * as T from '../funciones/interno/textos'
import { PaginaCargando } from '../marco/PaginaCargando'
import { useTitulo } from '../marco/useTitulo'

/**
 * JQ-12 · CU-17. La cola de reportes abiertos (API-52, sin los propios) y la decisión de cada uno
 * (API-53): mantener o retirar, siempre con motivo. Lo resuelto sale de la lista sin recargar.
 */
export function ReportesDeResenas() {
  useTitulo(T.TITULO_REPORTES)
  const [cola, setCola] = useState<ReporteEnCola[] | null>(null)
  const [error, setError] = useState(false)
  const [recarga, setRecarga] = useState(0)
  const [hecho, setHecho] = useState<string | null>(null)

  useEffect(() => {
    let vigente = true
    colaDeReportes()
      .then((c) => { if (vigente) { setCola(c); setError(false) } })
      .catch(() => { if (vigente) setError(true) })
    return () => { vigente = false }
  }, [recarga])

  function resuelto(id: string, decision: DecisionDeModeracion) {
    setCola((c) => c?.filter((r) => r.reporteId !== id) ?? null)
    setHecho(T.DECIDIDA[decision])
  }

  if (error) {
    return (
      <div className="contenedor ficha-pagina">
        <h1 className="pagina-titulo">{T.TITULO_REPORTES}</h1>
        <p className="aviso-linea" role="alert">
          {T.ERROR_REPORTES}{' '}
          <button type="button" className="enlace" onClick={() => { setError(false); setRecarga((n) => n + 1) }}>
            {T.REINTENTAR}
          </button>
        </p>
      </div>
    )
  }
  if (cola === null) return <PaginaCargando titulo={T.TITULO_REPORTES} />

  return (
    <div className="contenedor ficha-pagina mesa-verificacion">
      <h1 className="pagina-titulo">{T.TITULO_REPORTES}</h1>
      <p className="bloque-texto"><Link className="enlace" to="/interno">{T.VOLVER_AL_PANEL}</Link></p>
      <p className="bloque-texto" role="status">{hecho}</p>
      {cola.length === 0 && <p className="bloque-texto bloque-vacio">{T.REPORTES_VACIOS}</p>}
      {cola.length > 0 && (
        <ol className="cola-verificacion" aria-label={T.TITULO_REPORTES}>
          {cola.map((r) => (
            <li key={r.reporteId} className="bloque">
              <Reporte reporte={r} alResolver={(d) => resuelto(r.reporteId, d)} />
            </li>
          ))}
        </ol>
      )}
    </div>
  )
}

function Reporte({ reporte: r, alResolver }: {
  reporte: ReporteEnCola
  alResolver: (d: DecisionDeModeracion) => void
}) {
  const [decision, setDecision] = useState<DecisionDeModeracion | null>(null)
  const [motivo, setMotivo] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  const id = r.reporteId

  async function guardar(e: FormEvent) {
    e.preventDefault()
    if (decision === null) return
    const texto = motivo.trim()
    if (texto.length < MOTIVO_MODERACION_MINIMO) {
      setError(T.faltaMotivoDecision(MOTIVO_MODERACION_MINIMO))
      return
    }
    setError(null)
    setEnviando(true)
    try {
      await resolverReporte(id, decision, texto)
      alResolver(decision)
    } catch (err) {
      setError(err instanceof ErrorApi
        ? (Object.values(err.campos).join(' ') || err.paraMostrar)
        : 'No se pudo completar la operación.')
      setEnviando(false)
    }
  }

  return (
    <article aria-label={T.RESENA_DE(r.perfilNombre)}>
      <h2>{T.RESENA_DE(r.perfilNombre)}</h2>
      <p className="bloque-texto">
        <span>{T.estrellas(r.calificacion)}</span>
        {r.estadoResena === 'OcultaEnModeracion' && <> · <span>{T.RESENA_OCULTA}</span></>}
      </p>
      <blockquote className="bloque-texto">{r.comentario}</blockquote>
      <dl className="bloque-texto">
        <dt>{T.ETIQUETA_MOTIVO_REPORTE}</dt>
        <dd>{T.etiquetaDeMotivoDeReporte(r.motivo)}</dd>
        {r.detalle && <><dt>{T.ETIQUETA_DETALLE_REPORTE}</dt><dd>{r.detalle}</dd></>}
        <dt>{T.REPORTADA_EL}</dt>
        <dd>{fechaYHora(r.creadoEn)}</dd>
      </dl>

      <form className="editor" onSubmit={(e) => void guardar(e)} noValidate>
        <fieldset className="campo">
          <legend className="campo-etiqueta">{T.ETIQUETA_DECISION}</legend>
          {T.DECISIONES.map((d) => (
            <label key={d.valor} className="opcion">
              <input
                type="radio" name={`decision-${id}`} value={d.valor}
                checked={decision === d.valor}
                onChange={() => { setDecision(d.valor); setError(null) }}
              />
              {d.texto}
            </label>
          ))}
        </fieldset>
        <div className="campo">
          <label className="campo-etiqueta" htmlFor={`m-${id}`}>{T.ETIQUETA_MOTIVO_DECISION}</label>
          <textarea
            id={`m-${id}`} className="campo-entrada" rows={3} maxLength={1000}
            value={motivo} onChange={(e) => setMotivo(e.target.value)}
            aria-describedby={`m-${id}-ayuda`}
          />
          <p id={`m-${id}-ayuda`} className="letra-chica">{T.ayudaMotivoDecision(MOTIVO_MODERACION_MINIMO)}</p>
        </div>
        {error && <p className="campo-error" role="alert">{error}</p>}
        <div className="editor-acciones">
          <button type="submit" className="boton boton--tinta" disabled={decision === null || enviando}>
            {enviando ? T.RESOLVIENDO : T.BOTON_DECIDIR}
          </button>
        </div>
      </form>
    </article>
  )
}
