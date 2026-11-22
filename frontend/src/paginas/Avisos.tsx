import { useEffect, useState } from 'react'
import { Navigate } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import { agrupar, guardarGrupo, verPreferencias, type GrupoDeAvisos, type PreferenciaTipo } from '../funciones/avisos/preferencias'
import { activarPush, desactivarPush, estadoPush, type EntornoPush, type EstadoPush } from '../funciones/avisos/push'
import { PaginaCargando } from '../marco/PaginaCargando'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'
import { useSesion } from '../sesion/contexto'

const RUTA = '/avisos'
const TITULO = 'Avisos'

/*
 * ES-3 (CU-15, CS-14). Arriba, activar el push en ESTE navegador (API-45); abajo, por que canal
 * llega cada grupo de avisos (API-43). En ES-4 esta pantalla pasa a ser una seccion de «Mi cuenta».
 *
 * Solo clases que ya existen: el CSS es de Joaquin.
 *
 * TEXTOS PROPUESTOS, pendientes de aprobacion.
 */
const EXPLICACION: Record<EstadoPush, string> = {
  activo: 'Los avisos están activados en este dispositivo.',
  inactivo: 'Activa los avisos para enterarte aquí mismo cuando te confirmen, cambien o cancelen una hora, y antes de cada cita.',
  bloqueado: 'Bloqueaste los avisos de ZASS! en este navegador. Para activarlos, permítelos en la configuración del navegador y vuelve a esta página.',
  'no-soportado': 'Este navegador no puede recibir avisos. En iPhone, primero agrega ZASS! a tu pantalla de inicio y ábrelo desde ahí.',
  'sin-clave': 'Los avisos en el dispositivo todavía no están disponibles.',
}

const GRUPOS: Record<string, { titulo: string; detalle: string }> = {
  citas: { titulo: 'Citas', detalle: 'Cuando te piden, confirman, rechazan, cancelan o cambian una hora.' },
  recordatorios: { titulo: 'Recordatorios', detalle: 'Un día antes y dos horas antes de cada cita.' },
  resenas: { titulo: 'Reseñas', detalle: 'Cuando te dejan una reseña o responden la tuya.' },
  perfil: { titulo: 'Tu perfil', detalle: 'Cuando revisamos tu credencial, cuando vence, cuando un borrador se va a eliminar y cuando publicamos tu perfil.' },
  cobros: { titulo: 'Plan, cobros y tarjeta', detalle: 'Cuando tu plan está por vencer o venció, si un cobro no pasó y cuando se activa tu tarjeta.' },
}
const CORREO_FIJO = 'Estos avisos siempre te llegan por correo.'
const NINGUNO = 'Con los dos apagados no te llegará ningún aviso de este tipo.'
const PUSH_SIN_DISPOSITIVO = 'El push solo te llega en los dispositivos donde activaste los avisos.'

export function Avisos({ entorno }: { entorno?: EntornoPush }) {
  useMeta({ noindex: true })
  useTitulo(TITULO)
  const { estado: sesion } = useSesion()
  const [estado, setEstado] = useState<EstadoPush | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [trabajando, setTrabajando] = useState(false)
  const [preferencias, setPreferencias] = useState<PreferenciaTipo[] | null>(null)
  const [errorPref, setErrorPref] = useState<string | null>(null)
  const [guardando, setGuardando] = useState<string | null>(null)

  useEffect(() => {
    if (sesion !== 'dentro') return
    let vigente = true
    estadoPush(entorno)
      .then((e) => { if (vigente) setEstado(e) })
      .catch(() => { if (vigente) { setEstado('inactivo'); setError('No pudimos revisar los avisos. Intenta de nuevo.') } })
    verPreferencias()
      .then((p) => { if (vigente) setPreferencias(p) })
      .catch(() => { if (vigente) { setPreferencias([]); setErrorPref('No pudimos cargar tus preferencias de aviso. Intenta de nuevo.') } })
    return () => { vigente = false }
  }, [sesion, entorno])

  if (sesion === 'cargando' || (sesion === 'dentro' && (estado === null || preferencias === null))) {
    return <PaginaCargando titulo={TITULO} />
  }
  if (sesion === 'fuera') return <Navigate to="/entrar" replace state={{ despues: RUTA }} />

  async function cambiar(accion: (e?: EntornoPush) => Promise<EstadoPush>, fallo: string) {
    setError(null)
    setTrabajando(true)
    try {
      setEstado(await accion(entorno))
    } catch (e) {
      setError(mensajeDe(e, fallo))
    } finally {
      setTrabajando(false)
    }
  }

  async function elegir(grupo: GrupoDeAvisos, correo: boolean, push: boolean) {
    setErrorPref(null)
    setGuardando(grupo.clave)
    try {
      setPreferencias(await guardarGrupo(grupo, correo, push))
    } catch (e) {
      setErrorPref(mensajeDe(e, 'No pudimos guardar el cambio. Intenta de nuevo.'))
    } finally {
      setGuardando(null)
    }
  }

  const actual = estado ?? 'inactivo'
  const grupos = agrupar(preferencias ?? [])
  // Sin claves VAPID en el servidor el push no existe para nadie: elegirlo no tendria efecto.
  const hayPush = actual !== 'sin-clave'
  return (
    <div className="contenedor ficha-pagina">
      <h1 className="pagina-titulo">{TITULO}</h1>

      <section className="bloque" aria-labelledby="avisos-dispositivo">
        <h2 id="avisos-dispositivo">En este dispositivo</h2>
        <p className="bajada">{EXPLICACION[actual]}</p>
        {error && <p className="aviso-linea" role="alert">{error}</p>}

        {actual === 'inactivo' && (
          <button
            type="button" className="boton boton--amarillo boton--lleno" disabled={trabajando}
            onClick={() => void cambiar(activarPush, 'No pudimos activar los avisos. Intenta de nuevo.')}
          >
            {trabajando ? 'Activando...' : 'Activar avisos en este dispositivo'}
          </button>
        )}
        {actual === 'activo' && (
          <button
            type="button" className="boton" disabled={trabajando}
            onClick={() => void cambiar(desactivarPush, 'No pudimos desactivar los avisos. Intenta de nuevo.')}
          >
            {trabajando ? 'Desactivando...' : 'Desactivar en este dispositivo'}
          </button>
        )}
      </section>

      <h2 className="seccion-titulo">Qué avisos quieres recibir</h2>
      {hayPush && <p className="letra-chica">{PUSH_SIN_DISPOSITIVO}</p>}
      {errorPref && <p className="aviso-linea" role="alert">{errorPref}</p>}

      {grupos.map((g) => {
        const textos = GRUPOS[g.clave] ?? { titulo: g.clave, detalle: '' }
        const ocupado = guardando !== null
        return (
          <section key={g.clave} className="bloque" aria-labelledby={`grupo-${g.clave}`}>
            <h2 id={`grupo-${g.clave}`}>{textos.titulo}</h2>
            <p className="letra-chica">{textos.detalle}</p>
            <label className="casilla">
              <input
                type="checkbox" checked={g.correo} disabled={g.correoFijo || ocupado}
                onChange={(e) => void elegir(g, e.target.checked, g.push)}
              />
              Correo
            </label>
            {hayPush && (
              <>
                {' '}
                <label className="casilla">
                  <input
                    type="checkbox" checked={g.push} disabled={ocupado}
                    onChange={(e) => void elegir(g, g.correo, e.target.checked)}
                  />
                  Push
                </label>
              </>
            )}
            {g.correoFijo && <p className="letra-chica">{CORREO_FIJO}</p>}
            {!g.correo && (!g.push || !hayPush) && <p className="letra-chica">{NINGUNO}</p>}
          </section>
        )
      })}
    </div>
  )
}

function mensajeDe(e: unknown, fallo: string): string {
  return e instanceof ErrorApi && e.estado !== 0 && e.estado < 500 ? e.mensaje : fallo
}
