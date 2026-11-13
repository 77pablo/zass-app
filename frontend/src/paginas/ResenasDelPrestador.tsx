import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import { TarjetaDeResena } from '../componentes/resenas/TarjetaDeResena'
import { fichaPublica, type FichaPublica } from '../funciones/busqueda/servicio'
import { miPerfil } from '../funciones/perfil/servicio'
import { resenasPublicas, type ResenaPublica } from '../funciones/resenas/servicio'
import { useSesion } from '../sesion/contexto'
import { useTitulo } from '../marco/useTitulo'
import { NoEncontrada } from './NoEncontrada'

function mensajeDe(e: unknown, porDefecto: string): string {
  return e instanceof ErrorApi ? e.mensaje : porDefecto
}

type Carga =
  | { tipo: 'cargando' }
  | { tipo: 'ok'; ficha: FichaPublica; resenas: ResenaPublica[]; cursor: string | null }
  | { tipo: 'no-existe' }
  | { tipo: 'error'; mensaje: string }

/**
 * Lo que dicen de alguien, entero: la reputacion (RN-S5-10 y RN-S5-11) y las reseñas una por
 * una, con su respuesta. Es publica, como la ficha: leer no pide cuenta. Lo que si pide cuenta
 * es escribir, responder o reportar, y eso lo decide cada tarjeta.
 */
export function ResenasDelPrestador() {
  const { slug = '' } = useParams()
  const { estado: sesion, usuario } = useSesion()
  const [carga, setCarga] = useState<Carga>({ tipo: 'cargando' })
  const [intento, setIntento] = useState(0)
  const [miPerfilId, setMiPerfilId] = useState<string | null>(null)
  const [masFallo, setMasFallo] = useState<string | null>(null)
  const [cargandoMas, setCargandoMas] = useState(false)

  const esPrestador = usuario?.roles.includes('prestador') ?? false

  useTitulo(carga.tipo === 'ok' ? `Reseñas de ${carga.ficha.nombrePublico}` : 'Reseñas')

  useEffect(() => {
    let vigente = true
    fichaPublica(slug)
      .then(async (ficha) => ({ ficha, pagina: await resenasPublicas(slug) }))
      .then(({ ficha, pagina }) => {
        if (vigente) {
          setCarga({ tipo: 'ok', ficha, resenas: pagina.resenas, cursor: pagina.siguienteCursor })
        }
      })
      .catch((e: unknown) => {
        if (!vigente) return
        if (e instanceof ErrorApi && e.estado === 404) setCarga({ tipo: 'no-existe' })
        else setCarga({ tipo: 'error', mensaje: mensajeDe(e, 'No pudimos cargar las reseñas.') })
      })
    return () => { vigente = false }
  }, [slug, intento])

  useEffect(() => {
    if (sesion !== 'dentro' || !esPrestador) return
    let vigente = true
    // Sin ficha propia todavia no es un error: solo significa que ninguna reseña es suya.
    miPerfil().then((p) => { if (vigente) setMiPerfilId(p.id) }).catch(() => {})
    return () => { vigente = false }
  }, [sesion, esPrestador])

  const recargar = useCallback(() => {
    setCarga({ tipo: 'cargando' })
    setIntento((n) => n + 1)
  }, [])

  async function verMas() {
    if (carga.tipo !== 'ok' || carga.cursor === null || cargandoMas) return
    setCargandoMas(true); setMasFallo(null)
    try {
      const pagina = await resenasPublicas(slug, carga.cursor)
      setCarga((previo) => previo.tipo === 'ok'
        ? { ...previo, resenas: [...previo.resenas, ...pagina.resenas], cursor: pagina.siguienteCursor }
        : previo)
    } catch (e) {
      setMasFallo(mensajeDe(e, 'No pudimos cargar más reseñas.'))
    } finally {
      setCargandoMas(false)
    }
  }

  if (carga.tipo === 'no-existe') return <NoEncontrada />
  if (carga.tipo === 'cargando') {
    return <div className="contenedor ficha-pagina"><p className="cargando-linea" role="status">Cargando reseñas…</p></div>
  }
  if (carga.tipo === 'error') {
    return (
      <div className="contenedor ficha-pagina">
        <p className="aviso-linea" role="alert">
          {carga.mensaje}{' '}
          <button type="button" className="enlace" onClick={recargar}>Reintentar</button>
        </p>
      </div>
    )
  }

  const { ficha, resenas, cursor } = carga
  // Quien mira es el dueño del perfil reseñado. Se pide la sesion ADEMAS del perfil porque el
  // id guardado sobrevive a cerrar sesion, y sin esa condicion un perfil ajeno heredaria los
  // botones del dueño durante el primer pintado.
  const mia = sesion === 'dentro' && esPrestador && miPerfilId !== null && miPerfilId === ficha.id

  return (
    <div className="contenedor ficha-pagina resenas-pagina">
      <Link className="volver" to={`/prestadores/${slug}`}>Volver a la ficha de {ficha.nombrePublico}</Link>
      <h1 className="pagina-titulo">Lo que dicen de {ficha.nombrePublico}</h1>

      <Reputacion promedio={ficha.promedioCalificacion} total={ficha.totalResenas} />

      {/* Reseñar es de quien busca, no del dueño del perfil: ofrecerselo a el seria invitarlo a
          una autorreseña que el servidor rechaza (CU-16 4G). */}
      {!mia && (
        <p className="bloque-texto">
          <Link className="boton boton--amarillo" to={`/prestadores/${slug}/resena`}>
            Dejar mi reseña
          </Link>
        </p>
      )}

      {resenas.length === 0 ? (
        <p className="bloque-texto bloque-vacio">Todavía no hay reseñas escritas.</p>
      ) : (
        <ul className="resenas-lista">
          {resenas.map((r) => (
            <li key={r.id}>
              <TarjetaDeResena
                resena={r}
                puedeReportar={sesion === 'dentro'}
                puedeResponder={mia}
                alCambiar={recargar}
              />
            </li>
          ))}
        </ul>
      )}

      {cursor !== null && (
        <button type="button" className="enlace" disabled={cargandoMas} onClick={() => { void verMas() }}>
          {cargandoMas ? 'Cargando…' : 'Ver más reseñas'}
        </button>
      )}
      {masFallo !== null && <p className="aviso-linea" role="alert">{masFallo}</p>}
    </div>
  )
}

/**
 * RN-S5-10 y RN-S5-11: promedio simple y recuento a la vista, sin ponderacion; y con menos de
 * tres reseñas se muestra CUANTAS hay pero no el promedio, porque un promedio de una o dos
 * opiniones aparenta una solidez que no tiene. El servidor manda el promedio nulo en ese caso, y
 * la pantalla no lo calcula por su cuenta.
 */
function Reputacion({ promedio, total }: { promedio: number | null; total: number }) {
  if (total === 0) return null
  const resenas = total === 1 ? '1 reseña' : `${total} reseñas`
  return (
    <p className="nota">
      {promedio !== null && (
        <span className="nota-promedio">{promedio.toFixed(1).replace('.', ',')}</span>
      )}
      <span className="nota-detalle">
        {promedio !== null
          ? `de 5, según ${resenas}`
          : `${resenas}. El promedio se muestra desde la tercera.`}
      </span>
    </p>
  )
}
