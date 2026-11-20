import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import {
  colaDeVerificacion, minutosRestantes, MOTIVOS_DE_RECHAZO, resolverCredencial, tomarCredencial,
  urlDeDocumentoInterno,
  type CredencialEnCola, type MotivoDeRechazo, type ResultadoDeVerificacion,
} from '../funciones/interno/servicio'
import * as T from '../funciones/interno/textos'
import { PaginaCargando } from '../marco/PaginaCargando'
import { useTitulo } from '../marco/useTitulo'

/**
 * JQ-11 · CU-06. La mesa del Verificador: la cola (API-29), tomar (API-30, reserva de 30 min),
 * abrir los documentos de respaldo (API-74) y resolver (API-31).
 *
 * Las reglas las pone la API (largos mínimos del detalle, 20 caracteres si no hay documentos,
 * RN-S2-09): aquí solo se exige lo que no se puede mandar vacío, el motivo de un rechazo, y los
 * mensajes de la API se muestran tal cual.
 */
export function MesaDeVerificacion() {
  useTitulo(T.TITULO_MESA)
  const [cola, setCola] = useState<CredencialEnCola[] | null>(null)
  const [errorCola, setErrorCola] = useState(false)
  const [recarga, setRecarga] = useState(0)
  const [tomada, setTomada] = useState<CredencialEnCola | null>(null)
  const [tomandoId, setTomandoId] = useState<string | null>(null)
  const [aviso, setAviso] = useState<string | null>(null)
  const [hecho, setHecho] = useState<string | null>(null)

  useEffect(() => {
    if (tomada) return
    let vigente = true
    colaDeVerificacion()
      .then((c) => { if (vigente) { setCola(c); setErrorCola(false) } })
      .catch(() => { if (vigente) setErrorCola(true) })
    return () => { vigente = false }
  }, [recarga, tomada])

  async function tomar(c: CredencialEnCola) {
    setTomandoId(c.id)
    setAviso(null)
    setHecho(null)
    try {
      setTomada(await tomarCredencial(c.id))
    } catch (e) {
      // 409: otro la tomó o ya está resuelta. La API dice cuál; la cola se vuelve a pedir.
      setAviso(mensajeDeError(e))
      setRecarga((n) => n + 1)
    } finally {
      setTomandoId(null)
    }
  }

  function volver(mensaje: string | null = null) {
    setTomada(null)
    setHecho(mensaje)
    setRecarga((n) => n + 1)
  }

  if (tomada) {
    return <CredencialTomada credencial={tomada} alResolver={(m) => volver(m)} alVolver={() => volver()} />
  }

  if (errorCola) {
    return (
      <div className="contenedor ficha-pagina">
        <h1 className="pagina-titulo">{T.TITULO_MESA}</h1>
        <p className="aviso-linea" role="alert">
          {T.ERROR_COLA}{' '}
          <button type="button" className="enlace" onClick={() => { setErrorCola(false); setRecarga((n) => n + 1) }}>
            {T.REINTENTAR}
          </button>
        </p>
      </div>
    )
  }
  if (cola === null) return <PaginaCargando titulo={T.TITULO_MESA} />

  return (
    <div className="contenedor ficha-pagina mesa-verificacion">
      <h1 className="pagina-titulo">{T.TITULO_MESA}</h1>
      <p className="bloque-texto"><Link className="enlace" to="/interno">{T.VOLVER_AL_PANEL}</Link></p>
      {hecho && <p className="bloque-texto" role="status">{hecho}</p>}
      {aviso && <p className="aviso aviso--malo" role="alert">{aviso}</p>}
      {cola.length === 0 && <p className="bloque-texto bloque-vacio">{T.COLA_VACIA}</p>}
      {cola.length > 0 && (
        <ol className="cola-verificacion" aria-label={T.TITULO_MESA}>
          {cola.map((c) => (
            <li key={c.id} className="bloque">
              <Resumen c={c} />
              <p className="bloque-texto letra-chica">{T.ETIQUETA_EN_COLA_DESDE} {fechaYHora(c.enColaDesde)}</p>
              <button
                type="button" className="boton boton--chico"
                disabled={tomandoId !== null}
                onClick={() => void tomar(c)}
                aria-label={`${c.reservadaPorMi ? T.BOTON_SEGUIR : T.BOTON_TOMAR}: ${c.nombre}, ${c.prestador}`}
              >
                {tomandoId === c.id ? T.TOMANDO : c.reservadaPorMi ? T.BOTON_SEGUIR : T.BOTON_TOMAR}
              </button>
            </li>
          ))}
        </ol>
      )}
    </div>
  )
}

/** Lo que se ve de una credencial en la cola y al tomarla. */
function Resumen({ c }: { c: CredencialEnCola }) {
  return (
    <>
      <h2>{T.TIPO[c.tipo]}: {c.nombre}</h2>
      <p className="bloque-texto">
        <a className="enlace" href={`/prestadores/${encodeURIComponent(c.prestadorSlug)}`} target="_blank" rel="noopener noreferrer">
          {c.prestador}
        </a>
        {' · '}{T.ETIQUETA_ENTIDAD}: {c.entidad ?? T.SIN_ENTIDAD}
      </p>
      <ul className="etiquetas-credencial">
        {c.entidadReconocida === false && <li className="etiqueta etiqueta--alerta">{T.ENTIDAD_NO_RECONOCIDA}</li>}
        {c.sinDocumentos
          ? <li className="etiqueta etiqueta--alerta">{T.SIN_DOCUMENTOS}</li>
          : <li className="etiqueta">{T.documentosEnCola(c.documentos)}</li>}
        {c.vencimientoPasado && <li className="etiqueta etiqueta--alerta">{T.VENCIMIENTO_PASADO}</li>}
      </ul>
    </>
  )
}

function CredencialTomada({ credencial: c, alResolver, alVolver }: {
  credencial: CredencialEnCola
  alResolver: (mensaje: string) => void
  alVolver: () => void
}) {
  useTitulo(T.TITULO_MESA)
  const [ahora, setAhora] = useState(() => Date.now())
  useEffect(() => {
    const t = setInterval(() => setAhora(Date.now()), 15_000)
    return () => clearInterval(t)
  }, [])
  const minutos = minutosRestantes(c.reservadaHasta, ahora)
  const respaldo = c.respaldo ?? []

  return (
    <div className="contenedor ficha-pagina mesa-verificacion">
      <h1 className="pagina-titulo">{T.TITULO_MESA}</h1>
      <p className="bloque-texto">
        <button type="button" className="enlace" onClick={alVolver}>{T.VOLVER_A_LA_COLA}</button>
      </p>
      <section className="bloque" aria-label={c.nombre}>
        <Resumen c={c} />
        <p className="bloque-texto" role="timer" aria-live="off">
          {minutos > 0 ? T.reservaRestante(minutos) : T.RESERVA_VENCIDA}
        </p>
      </section>

      <section className="bloque">
        <h2>{T.TITULO_DOCUMENTOS}</h2>
        {c.sinDocumentos && <p className="bloque-texto">{T.SIN_DOCUMENTOS_DETALLE}</p>}
        {!c.sinDocumentos && respaldo.length === 0 && (
          <p className="aviso-linea">{T.DOCUMENTOS_NO_LLEGARON}</p>
        )}
        {respaldo.length > 0 && (
          <ul className="documentos-respaldo">
            {respaldo.map((d) => (
              <li key={d.id}>
                {d.nombre}{' '}
                <a className="enlace" href={urlDeDocumentoInterno(c.id, d.id)} target="_blank" rel="noopener noreferrer">
                  {T.ABRIR_DOCUMENTO}{' '}<span className="solo-lector">{d.nombre} {T.ABRE_EN_PESTANA}</span>
                </a>
              </li>
            ))}
          </ul>
        )}
      </section>

      {minutos > 0 && <FormularioDeResolucion credencial={c} alResolver={alResolver} />}
    </div>
  )
}

function FormularioDeResolucion({ credencial: c, alResolver }: {
  credencial: CredencialEnCola
  alResolver: (mensaje: string) => void
}) {
  const [resultado, setResultado] = useState<ResultadoDeVerificacion | null>(null)
  const [motivo, setMotivo] = useState<MotivoDeRechazo | ''>('')
  const [detalle, setDetalle] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)

  async function guardar(e: FormEvent) {
    e.preventDefault()
    if (resultado === null) return
    if (resultado === 'Rechazada' && motivo === '') {
      setError(T.FALTA_MOTIVO)
      return
    }
    setError(null)
    setEnviando(true)
    try {
      const texto = detalle.trim()
      await resolverCredencial(c.id, {
        resultado,
        ...(resultado === 'Rechazada' && motivo !== '' ? { motivo } : {}),
        ...(texto !== '' ? { detalle: texto } : {}),
      })
      alResolver(T.RESUELTA[resultado])
    } catch (err) {
      setError(mensajeDeError(err))
    } finally {
      setEnviando(false)
    }
  }

  const ayuda = resultado === 'Rechazada' ? T.AYUDA_DETALLE_RECHAZO
    : resultado === 'AntecedentesSolicitados' ? T.AYUDA_DETALLE_ANTECEDENTES
      : T.AYUDA_DETALLE_VERIFICAR

  return (
    <form className="bloque editor" onSubmit={(e) => void guardar(e)} noValidate>
      <h2>{T.TITULO_RESOLVER}</h2>
      <fieldset className="campo">
        <legend className="campo-etiqueta">{T.ETIQUETA_RESULTADO}</legend>
        {T.RESULTADOS.map((r) => (
          <label key={r.valor} className="opcion">
            <input
              type="radio" name="resultado" value={r.valor}
              checked={resultado === r.valor}
              onChange={() => { setResultado(r.valor); setError(null) }}
            />
            {r.texto}
          </label>
        ))}
      </fieldset>

      {resultado === 'Rechazada' && (
        <div className="campo">
          <label className="campo-etiqueta" htmlFor="v-motivo">{T.ETIQUETA_MOTIVO}</label>
          <select
            id="v-motivo" className="campo-entrada" value={motivo}
            onChange={(e) => { setMotivo(e.target.value as MotivoDeRechazo | ''); setError(null) }}
          >
            <option value="">{T.ELEGIR_MOTIVO}</option>
            {MOTIVOS_DE_RECHAZO.map((m) => <option key={m} value={m}>{T.MOTIVO[m]}</option>)}
          </select>
        </div>
      )}

      {resultado !== null && (
        <div className="campo">
          <label className="campo-etiqueta" htmlFor="v-detalle">{T.ETIQUETA_DETALLE}</label>
          <textarea
            id="v-detalle" className="campo-entrada" rows={4} maxLength={1000}
            value={detalle} onChange={(e) => setDetalle(e.target.value)}
            aria-describedby="v-detalle-ayuda"
          />
          <p id="v-detalle-ayuda" className="letra-chica">
            {c.sinDocumentos && resultado === 'Verificada' ? T.SIN_DOCUMENTOS_DETALLE : ayuda}
          </p>
        </div>
      )}

      {error && <p className="campo-error" role="alert">{error}</p>}

      <div className="editor-acciones">
        <button type="submit" className="boton boton--tinta" disabled={resultado === null || enviando}>
          {enviando ? T.RESOLVIENDO : T.BOTON_RESOLVER}
        </button>
      </div>
    </form>
  )
}

/** El mensaje de la API tal cual; en un 400 de validación, el de cada campo. */
function mensajeDeError(e: unknown): string {
  if (e instanceof ErrorApi) {
    const campos = Object.values(e.campos)
    return campos.length > 0 ? campos.join(' ') : e.paraMostrar
  }
  return 'No se pudo completar la operación.'
}

/** «19 de noviembre, 14:05», en hora de Chile. */
function fechaYHora(iso: string): string {
  return new Intl.DateTimeFormat('es-CL', {
    day: 'numeric', month: 'long', hour: '2-digit', minute: '2-digit', timeZone: 'America/Santiago',
  }).format(new Date(iso))
}
