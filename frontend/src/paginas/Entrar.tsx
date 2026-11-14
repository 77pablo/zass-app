import { useState } from 'react'
import { Link, Navigate, useLocation } from 'react-router-dom'
import { Aviso } from '../componentes/Aviso'
import { Ingresar } from '../funciones/identidad/Ingresar'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'
import { useSesion } from '../sesion/contexto'
import { destinoDe } from '../sesion/destino'

export function Entrar() {
  useTitulo('Entrar')
  useMeta({ noindex: true })
  const { estado, usuario, recargar } = useSesion()
  const { state } = useLocation()

  // El ingreso son DOS pasos: el POST que abre la sesion y el GET que lee la cuenta. Si el primero
  // sale bien y el segundo falla, el formulario no tiene nada que decir (no le fallo nada) y el
  // proveedor de sesion deja a la persona 'fuera' a proposito. Resultado visto en el navegador (D3):
  // la contrasena era correcta, las cookies estaban puestas, y la pantalla de entrar se quedaba
  // MUDA. Estos dos estados son los que la hacen hablar.
  const [leyendoCuenta, setLeyendoCuenta] = useState(false)
  const [noPudimosLeerla, setNoPudimosLeerla] = useState(false)

  async function abrirSesion() {
    setNoPudimosLeerla(false)
    setLeyendoCuenta(true)
    await recargar()
    setLeyendoCuenta(false)
    // `recargar` no lanza: cuando puede, deja el estado en 'dentro', y entonces el `return` de
    // abajo ya se fue a otra pagina y este aviso no se ve. Si sigue aqui, es que no se pudo.
    setNoPudimosLeerla(true)
  }

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
        <Ingresar alEntrar={() => { void abrirSesion() }} />
        {leyendoCuenta && (
          <p className="cargando-linea" role="status">
            <span className="pulso" aria-hidden="true" />Abriendo tu cuenta…
          </p>
        )}
        {noPudimosLeerla && (
          <Aviso>
            Tu contraseña era correcta, pero no pudimos cargar tu cuenta.{' '}
            <button type="button" className="enlace" onClick={() => { void abrirSesion() }}>
              Reintentar
            </button>
          </Aviso>
        )}
        <p className="formulario-pie">
          ¿No tienes cuenta? <Link to="/crear-cuenta">Crea una</Link>
          <br />
          <Link to="/recuperar-contrasena">Olvidé mi contraseña</Link>
        </p>
      </div>
    </div>
  )
}
