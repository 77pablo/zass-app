import { useEffect, useState } from 'react'
import { Link, Navigate } from 'react-router-dom'
import { misMetricas, type ConteoEnVentanas, type MisMetricas } from '../funciones/metricas/servicio'
import * as T from '../funciones/metricas/textos'
import { PaginaCargando } from '../marco/PaginaCargando'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'
import { useSesion } from '../sesion/contexto'

/**
 * JQ-8 · MI-METRICAS. La actividad del prestador: contactos recibidos y escaneos de sus tarjetas,
 * en las mismas tres ventanas que «Mis tarjetas» (7 días, 30 días, desde siempre).
 */
export function MiActividad() {
  useMeta({ noindex: true })
  useTitulo(T.TITULO)
  const { estado, usuario } = useSesion()
  const esPrestador = usuario?.roles.includes('prestador') ?? false
  const [datos, setDatos] = useState<MisMetricas | null>(null)
  const [error, setError] = useState(false)
  const [recarga, setRecarga] = useState(0)

  useEffect(() => {
    if (estado !== 'dentro' || !esPrestador) return
    let vigente = true
    misMetricas()
      .then((d) => { if (vigente) { setDatos(d); setError(false) } })
      .catch(() => { if (vigente) setError(true) })
    return () => { vigente = false }
  }, [estado, esPrestador, recarga])

  if (estado === 'cargando') return <PaginaCargando titulo={T.TITULO} />
  if (estado === 'fuera') return <Navigate to="/entrar" replace state={{ despues: '/mi-actividad' }} />

  if (!esPrestador) {
    return (
      <div className="contenedor ficha-pagina">
        <h1 className="pagina-titulo">{T.TITULO}</h1>
        <p className="bloque-texto">
          {T.NO_PRESTADOR_ANTES}<Link to="/ofrecer-servicios">{T.NO_PRESTADOR_ENLACE}</Link>{T.NO_PRESTADOR_DESPUES}
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
            {T.REINTENTAR}
          </button>
        </p>
      </div>
    )
  }
  if (datos === null) return <PaginaCargando titulo={T.TITULO} />

  const canales = datos.contactosPorCanal30Dias
  return (
    <div className="contenedor ficha-pagina">
      <h1 className="pagina-titulo">{T.TITULO}</h1>
      <p className="bloque-texto">{T.INTRODUCCION}</p>

      <section className="bloque" aria-labelledby="act-contactos">
        <h2 id="act-contactos">{T.CONTACTOS}</h2>
        <p className="letra-chica">{T.CONTACTOS_AYUDA}</p>
        <Ventanas conteo={datos.contactos} />
        <h3>{T.POR_CANAL}</h3>
        <dl className="cifras">
          {(Object.keys(T.CANAL) as (keyof typeof T.CANAL)[]).map((c) => (
            <div key={c} className="cifra"><dt>{T.CANAL[c]}</dt><dd className="cifra-valor">{canales[c]}</dd></div>
          ))}
        </dl>
      </section>

      <section className="bloque" aria-labelledby="act-escaneos">
        <h2 id="act-escaneos">{T.ESCANEOS}</h2>
        <Ventanas conteo={datos.escaneos} />
      </section>
    </div>
  )
}

function Ventanas({ conteo }: { conteo: ConteoEnVentanas }) {
  return (
    <dl className="cifras">
      <div className="cifra"><dt>{T.ULTIMOS_7}</dt><dd className="cifra-valor">{conteo.ultimos7Dias}</dd></div>
      <div className="cifra"><dt>{T.ULTIMOS_30}</dt><dd className="cifra-valor">{conteo.ultimos30Dias}</dd></div>
      <div className="cifra"><dt>{T.DESDE_SIEMPRE}</dt><dd className="cifra-valor">{conteo.total}</dd></div>
    </dl>
  )
}
