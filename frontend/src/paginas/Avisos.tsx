import { useEffect, useState } from 'react'
import { Navigate } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import { activarPush, desactivarPush, estadoPush, type EntornoPush, type EstadoPush } from '../funciones/avisos/push'
import { PaginaCargando } from '../marco/PaginaCargando'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'
import { useSesion } from '../sesion/contexto'

const RUTA = '/avisos'
const TITULO = 'Avisos en este dispositivo'

/*
 * ES-3 (CU-15). Activar los avisos push en ESTE navegador. En el PR siguiente (API-43) se suman
 * las preferencias por tipo de aviso, y en ES-4 esta pantalla pasa a ser una seccion de «Mi cuenta».
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

export function Avisos({ entorno }: { entorno?: EntornoPush }) {
  useMeta({ noindex: true })
  useTitulo(TITULO)
  const { estado: sesion } = useSesion()
  const [estado, setEstado] = useState<EstadoPush | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [trabajando, setTrabajando] = useState(false)

  useEffect(() => {
    if (sesion !== 'dentro') return
    let vigente = true
    estadoPush(entorno)
      .then((e) => { if (vigente) setEstado(e) })
      .catch(() => { if (vigente) { setEstado('inactivo'); setError('No pudimos revisar los avisos. Intenta de nuevo.') } })
    return () => { vigente = false }
  }, [sesion, entorno])

  if (sesion === 'cargando' || (sesion === 'dentro' && estado === null)) return <PaginaCargando titulo={TITULO} />
  if (sesion === 'fuera') return <Navigate to="/entrar" replace state={{ despues: RUTA }} />

  async function cambiar(accion: (e?: EntornoPush) => Promise<EstadoPush>, fallo: string) {
    setError(null)
    setTrabajando(true)
    try {
      setEstado(await accion(entorno))
    } catch (e) {
      setError(e instanceof ErrorApi && e.estado !== 0 && e.estado < 500 ? e.mensaje : fallo)
    } finally {
      setTrabajando(false)
    }
  }

  const actual = estado ?? 'inactivo'
  return (
    <div className="contenedor ficha-pagina">
      <h1 className="pagina-titulo">{TITULO}</h1>
      <p className="bajada">{EXPLICACION[actual]}</p>
      <p className="bajada">Los correos te siguen llegando igual.</p>

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
    </div>
  )
}
