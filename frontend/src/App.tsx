import { useState } from 'react'
import { Ingresar } from './funciones/identidad/Ingresar'
import { Registrarse } from './funciones/identidad/Registrarse'
import { cerrarSesion, type Sesion } from './funciones/identidad/servicio'

type Pantalla = 'entrar' | 'crear'

export function App() {
  const [pantalla, setPantalla] = useState<Pantalla>('entrar')
  const [sesion, setSesion] = useState<Sesion | null>(null)

  async function salir() {
    try {
      await cerrarSesion()
    } finally {
      // Aunque la API falle, aqui se sale igual: dejar la pantalla como si
      // siguiera dentro es peor que cerrar de mas.
      setSesion(null)
      setPantalla('entrar')
    }
  }

  return (
    <main>
      <div className="marca">
        <p className="marca-nombre">ZAS<em>S!</em></p>
      </div>

      {sesion ? (
        <section className="tarjeta dentro">
          <h1 className="titulo">Ya estás dentro</h1>
          <p className="bajada">
            Entraste como <strong>{sesion.roles.join(', ') || 'sin rol'}</strong>.
          </p>
          {!sesion.correoVerificado && (
            <p className="aviso aviso--malo" role="status">
              Tu correo está <strong>sin verificar</strong>. Busca el enlace que te
              mandamos: hasta entonces tu cuenta esta a medias.
            </p>
          )}
          <button className="boton" type="button" onClick={salir}>Salir</button>
        </section>
      ) : pantalla === 'entrar' ? (
        <>
          <Ingresar alEntrar={setSesion} />
          <p className="pie">
            ¿No tienes cuenta?{' '}
            <button className="enlace" type="button" onClick={() => setPantalla('crear')}>
              Crear una
            </button>
          </p>
        </>
      ) : (
        <>
          <Registrarse alRegistrarse={() => {}} />
          <p className="pie">
            ¿Ya tienes cuenta?{' '}
            <button className="enlace" type="button" onClick={() => setPantalla('entrar')}>
              Entrar
            </button>
          </p>
        </>
      )}
    </main>
  )
}
