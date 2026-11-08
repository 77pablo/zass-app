import { useState } from 'react'
import { Link, Navigate, useNavigate } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import { Aviso } from '../componentes/Aviso'
import { activarPrestador } from '../funciones/identidad/servicio'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'
import { useSesion } from '../sesion/contexto'

/**
 * «Ofrecer mis servicios». Una persona busca hoy y ofrece mañana, y no debería tener que crear otra cuenta ni
 * salir de la que tiene: aquí activa su ficha dentro de la misma cuenta, y sigue pudiendo buscar.
 */
export function OfrecerServicios() {
  useTitulo('Ofrecer mis servicios')
  useMeta({ noindex: true })
  const { estado, usuario, recargar } = useSesion()
  const irA = useNavigate()
  const [enviando, setEnviando] = useState(false)
  const [fallo, setFallo] = useState<string>()

  if (estado === 'cargando') return null
  // Ya lo es: lo que quiere es su ficha.
  if (estado === 'dentro' && usuario?.roles.includes('prestador')) return <Navigate to="/mi-ficha" replace />

  async function activar() {
    setEnviando(true)
    setFallo(undefined)
    try {
      await activarPrestador()
      await recargar()   // los roles viajan en la sesion: sin esto «Mi ficha» no aparecería
      irA('/mi-ficha')
    } catch (e) {
      setFallo(e instanceof ErrorApi ? e.mensaje : 'No pudimos activarlo. Intenta de nuevo.')
      setEnviando(false)
    }
  }

  return (
    <div className="pagina-formulario">
      <div className="contenedor contenedor--angosto">
        <div className="tarjeta">
          <h1 className="titulo">Ofrece tus servicios</h1>
          <p className="bajada">
            Aparece ante quien te busca cerca de ti. Es la misma cuenta: sigues pudiendo buscar a otras personas.
          </p>
          {fallo && <Aviso>{fallo}</Aviso>}
          {estado === 'dentro' ? (
            <button className="boton" type="button" disabled={enviando} onClick={() => { void activar() }}>
              {enviando ? 'Activando…' : 'Activar mi ficha'}
            </button>
          ) : (
            <p className="bajada">
              Primero necesitas una cuenta. <Link to="/crear-cuenta?rol=prestador">Crear mi cuenta</Link>
              {' '}o <Link to="/entrar" state={{ despues: '/ofrecer-servicios' }}>entrar</Link>.
            </p>
          )}
        </div>
      </div>
    </div>
  )
}
