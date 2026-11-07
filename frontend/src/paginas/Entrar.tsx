import { Link, Navigate, useLocation } from 'react-router-dom'
import { Ingresar } from '../funciones/identidad/Ingresar'
import { useTitulo } from '../marco/useTitulo'
import { useSesion } from '../sesion/contexto'
import { destinoDe } from '../sesion/destino'

export function Entrar() {
  useTitulo('Entrar')
  const { estado, usuario, recargar } = useSesion()
  const { state } = useLocation()

  // Una sola puerta de salida: en cuanto la sesion queda abierta (o si ya lo estaba), se va a
  // donde la persona queria ir y, si no queria nada en particular, a lo que le sirve segun su
  // rol. Tener dos redirecciones competia: la generica ganaba y un profesional caia en la portada.
  if (estado === 'dentro') {
    const aPrestador = usuario?.roles.includes('prestador') ?? false
    return <Navigate to={destinoDe(state) ?? (aPrestador ? '/mi-ficha' : '/')} replace />
  }

  return (
    <div className="pagina-formulario">
      <div className="contenedor contenedor--angosto">
        <p className="sobretitulo sobretitulo--tinta">Bienvenido de vuelta</p>
        <Ingresar alEntrar={() => { void recargar() }} />
        <p className="formulario-pie">
          ¿No tienes cuenta? <Link to="/crear-cuenta">Crea una</Link>
          <br />
          <Link to="/recuperar-contrasena">Olvidé mi contraseña</Link>
        </p>
      </div>
    </div>
  )
}
