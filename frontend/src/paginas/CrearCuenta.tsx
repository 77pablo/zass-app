import { Link, useSearchParams } from 'react-router-dom'
import { Registrarse } from '../funciones/identidad/Registrarse'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'

export function CrearCuenta() {
  useTitulo('Crear cuenta')
  useMeta({ noindex: true })
  const [params] = useSearchParams()
  // Desde «Para profesionales» se llega con ?rol=prestador, y la cuenta parte ya como tal.
  const rolInicial = params.get('rol') === 'prestador' ? 'prestador' : 'buscador'

  return (
    <div className="pagina-formulario">
      <div className="contenedor contenedor--angosto">
        <p className="sobretitulo sobretitulo--tinta">Súmate</p>
        <Registrarse alRegistrarse={() => {}} rolInicial={rolInicial} />
        <p className="formulario-pie">
          ¿Ya tienes cuenta? <Link to="/entrar">Entra</Link>
        </p>
      </div>
    </div>
  )
}
