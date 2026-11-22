import { useState, type FormEvent } from 'react'
import { Link, Navigate } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import { Campo } from '../componentes/Campo'
import { eliminarMiCuenta } from '../funciones/identidad/servicio'
import * as T from '../funciones/identidad/textosDeCuenta'
import { PaginaCargando } from '../marco/PaginaCargando'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'
import { useSesion } from '../sesion/contexto'

/**
 * Mi cuenta. Por ahora: los datos y ELIMINAR la cuenta (CS-04, API-17), que no tenía ninguna
 * pantalla aunque la API existía. Es el derecho de supresión de la Ley 21.719, y Apple lo exige
 * para la app (ver PLAN-APP.md, riesgo D).
 *
 * Lo que decidió Pablo el 17-nov-2026, y la pantalla lo dice ANTES de confirmar: se pide la
 * contraseña; las horas futuras se cancelan y se le avisa a la otra persona; la suscripción del
 * prestador se cancela, sin reembolso.
 */
export function MiCuenta() {
  useMeta({ noindex: true })
  useTitulo(T.TITULO)
  const { estado, usuario, recargar } = useSesion()

  const [contrasena, setContrasena] = useState('')
  const [error, setError] = useState<string>()
  const [enviando, setEnviando] = useState(false)
  // Va ANTES de mirar la sesión: al eliminar, la sesión pasa a 'fuera' y, sin esto, la página
  // mandaría a /entrar en vez de decir que la cuenta se eliminó.
  const [eliminada, setEliminada] = useState(false)

  if (eliminada) {
    return (
      <div className="contenedor ficha-pagina">
        <h1 className="pagina-titulo">{T.ELIMINADA_TITULO}</h1>
        <p className="bloque-texto" role="status">{T.ELIMINADA_TEXTO}</p>
        <p className="bloque-texto"><Link className="boton" to="/">{T.VOLVER_AL_INICIO}</Link></p>
      </div>
    )
  }

  if (estado === 'cargando') return <PaginaCargando titulo={T.TITULO} />
  if (estado === 'fuera' || usuario === null) {
    return <Navigate to="/entrar" replace state={{ despues: '/mi-cuenta' }} />
  }

  const esPrestador = usuario.roles.includes('prestador')

  async function eliminar(e: FormEvent) {
    e.preventDefault()
    setEnviando(true)
    setError(undefined)
    try {
      await eliminarMiCuenta(contrasena)
      setEliminada(true)
      // La API ya cerró la sesión: que el resto del sitio (la cabecera) se entere.
      await recargar()
    } catch (err) {
      // 422 «La contraseña no es correcta.» y 400 «Escribe tu contraseña…» traen su texto.
      setError(err instanceof ErrorApi ? (err.campos.contrasena ?? err.mensaje) : T.ERROR_GENERICO)
    } finally {
      setEnviando(false)
    }
  }

  return (
    <div className="contenedor ficha-pagina">
      <h1 className="pagina-titulo">{T.TITULO}</h1>

      <section className="bloque">
        <dl className="datos-cuenta">
          <dt>{T.ETIQUETA_NOMBRE}</dt><dd>{usuario.nombre}</dd>
          <dt>{T.ETIQUETA_CORREO}</dt><dd>{usuario.correo}</dd>
        </dl>
      </section>

      <section className="bloque" aria-labelledby="eliminar-titulo">
        <h2 id="eliminar-titulo">{T.ELIMINAR_TITULO}</h2>
        <p className="bloque-texto">{T.ELIMINAR_ADVERTENCIA}</p>
        <ul className="bloque-texto lista-consecuencias">
          <li>{T.CONSECUENCIA_DATOS}</li>
          <li>{T.CONSECUENCIA_CITAS}</li>
          {esPrestador && <li>{T.CONSECUENCIA_PRESTADOR}</li>}
        </ul>

        <form className="eliminar-cuenta" onSubmit={(e) => void eliminar(e)} noValidate>
          <Campo
            id="eliminar-contrasena" etiqueta={T.ETIQUETA_CONTRASENA} tipo="password"
            valor={contrasena} alCambiar={setContrasena} error={error}
            autoComplete="current-password"
          />
          <button type="submit" className="boton boton--peligro" disabled={enviando || contrasena === ''}>
            {enviando ? T.ELIMINANDO : T.BOTON_ELIMINAR}
          </button>
        </form>
      </section>
    </div>
  )
}
