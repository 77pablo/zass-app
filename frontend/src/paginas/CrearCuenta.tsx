import { useState } from 'react'
import { Link, useLocation, useSearchParams } from 'react-router-dom'
import { Registrarse } from '../funciones/identidad/Registrarse'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'
import { destinoDe } from '../sesion/destino'

export function CrearCuenta() {
  useTitulo('Crear cuenta')
  useMeta({ noindex: true })
  const [params] = useSearchParams()
  const { state } = useLocation()
  // Desde «Para profesionales» se llega con ?rol=prestador, y la cuenta parte ya como tal.
  const rolInicial = params.get('rol') === 'prestador' ? 'prestador' : 'buscador'
  const [registrada, setRegistrada] = useState(false)

  // A donde queria ir la persona cuando se le pidio cuenta («Guardar» en una ficha, agendar,
  // ver el contacto). Se lo pasa a «Entrar», que es quien la devuelve ahi (destinoDe). Antes se
  // perdia aqui: el enlace a entrar iba sin estado y, tras entrar, la persona caia en la
  // portada en vez de en la ficha que estaba mirando (visto en un navegador, 21-nov).
  // Solo se reenvia un destino que destinoDe acepta: una ruta propia, nunca otro sitio.
  const destino = destinoDe(state)
  const estadoParaEntrar = destino ? { despues: destino } : undefined

  return (
    <div className="pagina-formulario">
      <div className="contenedor contenedor--angosto">
        <p className="sobretitulo sobretitulo--tinta">Súmate</p>
        <Registrarse alRegistrarse={() => setRegistrada(true)} rolInicial={rolInicial} />
        <p className="formulario-pie">
          {registrada ? (
            <>
              Cuando confirmes tu correo,{' '}
              <Link to="/entrar" state={estadoParaEntrar}>entra aquí</Link>
              {destino ? ' y vuelves a donde estabas.' : '.'}
            </>
          ) : (
            <>
              ¿Ya tienes cuenta? <Link to="/entrar" state={estadoParaEntrar}>Entra</Link>
            </>
          )}
        </p>
      </div>
    </div>
  )
}
