import { useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import { Aviso } from '../componentes/Aviso'
import { noFuiYo } from '../funciones/identidad/servicio'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'

/**
 * API-85 «No fui yo» (revisión de seguridad, 25-nov-2026). Se llega desde el correo que avisa que
 * se activó la verificación en dos pasos: /no-fui-yo?token=...
 *
 * NO actúa al abrirse, a diferencia de verificar el correo: los antivirus del correo abren los
 * enlaces solos, y esto cierra sesiones y deja la contraseña sin servir. Se actúa con el botón. Si
 * sale bien, lleva directo a elegir la contraseña nueva con el token que devuelve la API.
 */
export function NoFuiYo() {
  useTitulo('No fui yo')
  useMeta({ noindex: true })
  const [params] = useSearchParams()
  const token = params.get('token') ?? ''
  const navegar = useNavigate()
  const [enviando, setEnviando] = useState(false)
  const [aviso, setAviso] = useState<string>()

  async function quitar() {
    setEnviando(true)
    setAviso(undefined)
    try {
      const { tokenRestablecer } = await noFuiYo(token)
      navegar(`/restablecer-contrasena?token=${encodeURIComponent(tokenRestablecer)}`, { replace: true })
    } catch (e) {
      setAviso(e instanceof ErrorApi ? e.paraMostrar : 'Algo salió mal. Intenta de nuevo.')
      setEnviando(false)
    }
  }

  return (
    <div className="pagina-formulario">
      <div className="contenedor contenedor--angosto">
        <p className="sobretitulo sobretitulo--tinta">Tu cuenta</p>
        {token === '' ? (
          <div className="tarjeta">
            <h1 className="titulo">Enlace incompleto</h1>
            <p className="bajada">Abre el enlace que te llegó al correo.</p>
            <Link className="boton" to="/recuperar-contrasena">Crear una contraseña nueva</Link>
          </div>
        ) : (
          <div className="tarjeta">
            <h1 className="titulo">¿No activaste tú la verificación en dos pasos?</h1>
            <p className="bajada">
              Entonces alguien conoce tu contraseña. Al confirmar, quitamos la verificación que puso,
              cerramos todas las sesiones abiertas y tu contraseña actual deja de servir. Enseguida
              eliges una nueva.
            </p>
            {aviso && <Aviso>{aviso}</Aviso>}
            <button className="boton" type="button" disabled={enviando} onClick={() => void quitar()}>
              {enviando ? 'Quitando…' : 'No fui yo: quitarla'}
            </button>
          </div>
        )}
      </div>
    </div>
  )
}
