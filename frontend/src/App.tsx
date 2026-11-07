import { useState } from 'react'
import { Buscar } from './funciones/busqueda/Buscar'
import { Ingresar } from './funciones/identidad/Ingresar'
import { Registrarse } from './funciones/identidad/Registrarse'
import { cerrarSesion, type Sesion } from './funciones/identidad/servicio'

/**
 * La busqueda es la portada: no pide sesion (P1 de CU-08), asi que es lo primero que
 * ve alguien que nunca inicio sesion. Entrar y crear cuenta son pantallas a las que se
 * va desde ahi, no la puerta de entrada.
 */
type Vista = 'buscar' | 'entrar' | 'crear' | 'cuenta'

export function App() {
  const [vista, setVista] = useState<Vista>('buscar')
  const [sesion, setSesion] = useState<Sesion | null>(null)

  async function salir() {
    try {
      await cerrarSesion()
    } catch {
      // Aunque la API falle, aqui se sale igual: dejar la pantalla como si siguiera
      // dentro es peor que cerrar de mas. El catch NO es decorativo: sin el, el
      // finally sale de la pantalla pero la promesa rechazada escapa sin atrapar
      // (un "Uncaught (in promise)" en la consola) porque nadie espera a salir().
      //
      // Ojo con lo que esto deja pendiente: si el servidor no recibio el cierre, la
      // cookie de refresco sigue viva hasta que venza. La pantalla dice "saliste" y
      // el servidor aun no lo sabe. Decidir si eso merece avisarle a la persona.
    } finally {
      setSesion(null)
      setVista('buscar')
    }
  }

  return (
    <main>
      <div className="marca">
        <p className="marca-nombre">ZAS<em>S!</em></p>
      </div>

      {/* Solo se ofrece ir a donde NO se esta: el enlace a la propia pagina apuntaria a
          si mismo, y en las de ingreso duplicaria el boton "Entrar" del formulario. */}
      <nav className="navegacion" aria-label="Principal">
        {vista !== 'buscar' && (
          <button className="enlace" type="button" onClick={() => setVista('buscar')}>
            Buscar
          </button>
        )}
        {vista === 'buscar' && !sesion && (
          <button className="enlace" type="button" onClick={() => setVista('entrar')}>
            Entrar
          </button>
        )}
        {vista === 'buscar' && sesion && (
          <button className="enlace" type="button" onClick={() => setVista('cuenta')}>
            Mi cuenta
          </button>
        )}
      </nav>

      {/* La busqueda se esconde en vez de desmontarse: quien busca, mira resultados y se
          va a iniciar sesion no puede perder lo buscado al volver. Desmontarla
          reiniciaria el formulario y volveria a pedir el catalogo. */}
      <div hidden={vista !== 'buscar'}>
        <Buscar />
      </div>

      {vista !== 'buscar' && (
        sesion ? (
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
        ) : vista === 'crear' ? (
          <>
            <Registrarse alRegistrarse={() => {}} />
            <p className="pie">
              ¿Ya tienes cuenta?{' '}
              <button className="enlace" type="button" onClick={() => setVista('entrar')}>
                Entrar
              </button>
            </p>
          </>
        ) : (
          <>
            <Ingresar alEntrar={setSesion} />
            <p className="pie">
              ¿No tienes cuenta?{' '}
              <button className="enlace" type="button" onClick={() => setVista('crear')}>
                Crear una
              </button>
            </p>
          </>
        )
      )}
    </main>
  )
}
