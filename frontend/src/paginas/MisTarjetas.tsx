import { useEffect, useState } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { CodigoQr } from '../componentes/tarjetas/CodigoQr'
import {
  desactivarTarjeta, enlaceQr, enlaceVisible, estadisticasDeTarjeta, fechaDeTarjeta, llegaActivada, misTarjetas,
  type EstadisticasDeTarjeta, type MiTarjeta,
} from '../funciones/tarjetas/servicio'
import * as T from '../funciones/tarjetas/textos'
import { PaginaCargando } from '../marco/PaginaCargando'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'
import { useSesion } from '../sesion/contexto'

/**
 * Mis tarjetas (CU-20, API-56 a API-58). Listar y desactivar se permiten siempre, aunque la
 * suscripción esté vencida: perder la tarjeta no puede quedar sin salida.
 *
 * El enlace se muestra como TEXTO y no como `<a>`: si el prestador lo tocara desde aquí,
 * contaría como un escaneo de su propia tarjeta.
 */
export function MisTarjetas() {
  useMeta({ noindex: true })
  const { estado, usuario } = useSesion()
  useTitulo(T.TITULO)
  const esPrestador = usuario?.roles.includes('prestador') ?? false

  const [tarjetas, setTarjetas] = useState<MiTarjeta[] | null>(null)
  const [error, setError] = useState(false)
  const [recarga, setRecarga] = useState(0)
  // CU-19: al volver de activar una tarjeta, el aviso de que quedó activa. Se lee una vez y se
  // borra del historial: si no, recargar la página lo volvería a mostrar.
  const { pathname, state } = useLocation()
  const navegar = useNavigate()
  const [aviso, setAviso] = useState<string | null>(() => (llegaActivada(state) ? T.ACTIVADA_OK : null))
  useEffect(() => {
    if (llegaActivada(state)) navegar(pathname, { replace: true, state: null })
  }, [state, pathname, navegar])

  useEffect(() => {
    if (estado !== 'dentro' || !esPrestador) return
    let vigente = true
    misTarjetas()
      .then((t) => { if (vigente) { setTarjetas(t); setError(false) } })
      .catch(() => { if (vigente) setError(true) })
    return () => { vigente = false }
  }, [estado, esPrestador, recarga])

  if (estado === 'cargando') return <PaginaCargando titulo={T.TITULO} />
  if (estado === 'fuera') return <Navigate to="/entrar" replace state={{ despues: '/mis-tarjetas' }} />

  if (!esPrestador) {
    return (
      <div className="contenedor ficha-pagina">
        <h1 className="pagina-titulo">{T.TITULO}</h1>
        <p className="bloque-texto">
          {T.NO_PRESTADOR_ANTES}
          <Link to="/ofrecer-servicios">{T.NO_PRESTADOR_ENLACE}</Link>
          {T.NO_PRESTADOR_DESPUES}
        </p>
      </div>
    )
  }

  if (error) {
    return (
      <div className="contenedor ficha-pagina">
        <h1 className="pagina-titulo">{T.TITULO}</h1>
        <p className="aviso-linea" role="alert">
          {T.ERROR_CARGAR}{' '}
          <button type="button" className="enlace" onClick={() => { setError(false); setRecarga((n) => n + 1) }}>
            Reintentar
          </button>
        </p>
      </div>
    )
  }
  if (tarjetas === null) return <PaginaCargando titulo={T.TITULO} />

  const activas = tarjetas.filter((t) => t.estado === 'Activa')

  return (
    <div className="contenedor ficha-pagina mis-tarjetas">
      <h1 className="pagina-titulo">{T.TITULO}</h1>
      <p className="bloque-texto">{T.INTRODUCCION}</p>
      {aviso && <p className="bloque-texto" role="status">{aviso}</p>}
      {activas.length === 0 && <p className="bloque-texto bloque-vacio">{T.SIN_TARJETAS}</p>}

      <ul className="lista-tarjetas">
        {tarjetas.map((t) => (
          <li key={t.id}>
            <UnaTarjeta
              tarjeta={t}
              alDesactivar={() => { setAviso(T.DESACTIVADA_OK); setRecarga((n) => n + 1) }}
            />
          </li>
        ))}
      </ul>
    </div>
  )
}

function UnaTarjeta({ tarjeta, alDesactivar }: { tarjeta: MiTarjeta; alDesactivar: () => void }) {
  const activa = tarjeta.estado === 'Activa'
  const [verQr, setVerQr] = useState(false)
  const [confirmando, setConfirmando] = useState(false)
  const [ocupado, setOcupado] = useState(false)
  const [error, setError] = useState(false)

  const fecha = activa
    ? tarjeta.activadaEn && T.activadaEl(fechaDeTarjeta(tarjeta.activadaEn))
    : tarjeta.desactivadaEn && T.desactivadaEl(fechaDeTarjeta(tarjeta.desactivadaEn))

  async function desactivar() {
    setOcupado(true)
    setError(false)
    try {
      await desactivarTarjeta(tarjeta.id)
      setConfirmando(false)
      alDesactivar()
    } catch {
      setError(true)
    } finally {
      setOcupado(false)
    }
  }

  return (
    <article className="tarjeta-nfc" aria-label={tarjeta.urlEstable}>
      <p className="tarjeta-nfc-enlace">{enlaceVisible(tarjeta.urlEstable)}</p>
      <p className="tarjeta-detalle">
        {activa ? T.ACTIVA : T.DESACTIVADA}{fecha ? ` · ${fecha}` : ''}
      </p>

      {activa && (
        <>
          <button type="button" className="boton boton--chico" aria-expanded={verQr}
            onClick={() => setVerQr((v) => !v)}>
            {verQr ? T.OCULTAR_QR : T.MOSTRAR_QR}
          </button>
          {verQr && (
            <div className="tarjeta-nfc-qr">
              <CodigoQr texto={enlaceQr(tarjeta.urlEstable)} alt={T.altDelQr(tarjeta.urlEstable)} />
              <p className="tarjeta-detalle">{T.AYUDA_QR}</p>
            </div>
          )}
        </>
      )}

      <Estadisticas tarjetaId={tarjeta.id} />

      {activa && !confirmando && (
        <button type="button" className="boton boton--chico" onClick={() => setConfirmando(true)}>
          {T.DESACTIVAR}
        </button>
      )}
      {activa && confirmando && (
        <div role="group" aria-label={T.DESACTIVAR}>
          <p className="bloque-texto">{T.confirmarDesactivar(tarjeta.urlEstable)}</p>
          <button type="button" className="boton boton--chico boton--amarillo" disabled={ocupado}
            onClick={() => void desactivar()}>
            {T.SI_DESACTIVAR}
          </button>{' '}
          <button type="button" className="boton boton--chico" disabled={ocupado}
            onClick={() => { setConfirmando(false); setError(false) }}>
            {T.CANCELAR}
          </button>
        </div>
      )}
      {error && <p className="aviso-linea" role="alert">{T.ERROR_DESACTIVAR}</p>}
    </article>
  )
}

/** Si no cargan, no se muestra nada: las cifras son un extra, no lo que la pantalla resuelve. */
function Estadisticas({ tarjetaId }: { tarjetaId: string }) {
  const [e, setE] = useState<EstadisticasDeTarjeta | null>(null)

  useEffect(() => {
    let vigente = true
    estadisticasDeTarjeta(tarjetaId)
      .then((x) => { if (vigente) setE(x) })
      .catch(() => { /* sin cifras */ })
    return () => { vigente = false }
  }, [tarjetaId])

  if (e === null) return null

  const filas: [string, EstadisticasDeTarjeta['total']][] = [
    [T.ULTIMOS_7, e.ultimos7Dias], [T.ULTIMOS_30, e.ultimos30Dias], [T.DESDE_SIEMPRE, e.total],
  ]
  return (
    <div className="tarjeta-nfc-cifras">
      <table>
        <caption>{T.ESCANEOS}</caption>
        <thead>
          <tr><td /><th scope="col">{T.CON_NFC}</th><th scope="col">{T.CON_QR}</th><th scope="col">{T.ESCANEOS}</th></tr>
        </thead>
        <tbody>
          {filas.map(([nombre, c]) => (
            <tr key={nombre}>
              <th scope="row">{nombre}</th><td>{c.nfc}</td><td>{c.qr}</td><td>{c.total}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="tarjeta-detalle">{T.QUE_CONTAMOS}</p>
    </div>
  )
}
