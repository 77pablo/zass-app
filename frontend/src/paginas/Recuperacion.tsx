import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import { Aviso } from '../componentes/Aviso'
import { Campo } from '../componentes/Campo'
import {
  pedirRecuperacion, restablecerContrasena, verificarCorreo,
} from '../funciones/identidad/servicio'
import { Icono } from '../marco/Icono'
import { useTitulo } from '../marco/useTitulo'

const CONTRASENA_MINIMA = 10
const CONTRASENA_MAXIMA = 128

function mensajeDe(e: unknown): string {
  return e instanceof ErrorApi ? e.mensaje : 'Algo salió mal. Intenta de nuevo.'
}

/** Las tres pantallas comparten el marco: una tarjeta angosta sobre la pagina. */
function Marco({ sobretitulo, children }: { sobretitulo: string; children: React.ReactNode }) {
  return (
    <div className="pagina-formulario">
      <div className="contenedor contenedor--angosto">
        <p className="sobretitulo sobretitulo--tinta">{sobretitulo}</p>
        {children}
      </div>
    </div>
  )
}

/**
 * API-13. Se llega desde el enlace del correo: /verificar-correo?token=...
 *
 * El token se consume UNA vez. En desarrollo, StrictMode monta el efecto dos veces; si cada
 * montaje llamara a la API, el segundo recibiria "enlace ya usado" y taparia el exito del
 * primero. Por eso la llamada vive en una ref (una sola promesa) y cada montaje solo se
 * suscribe a ella.
 */
export function VerificarCorreo() {
  useTitulo('Verificar correo')
  const [params] = useSearchParams()
  const token = params.get('token') ?? ''
  const llamada = useRef<Promise<void> | null>(null)
  const [resultado, setResultado] = useState<'cargando' | 'listo' | string>(
    token === '' ? 'Este enlace está incompleto. Abre el que te llegó al correo.' : 'cargando',
  )

  useEffect(() => {
    if (token === '') return
    let vigente = true
    llamada.current ??= verificarCorreo(token)
    llamada.current
      .then(() => { if (vigente) setResultado('listo') })
      .catch((e: unknown) => { if (vigente) setResultado(mensajeDe(e)) })
    return () => { vigente = false }
  }, [token])

  return (
    <Marco sobretitulo="Tu correo">
      <div className="tarjeta">
        {resultado === 'cargando' && <p role="status">Verificando tu correo…</p>}
        {resultado === 'listo' && (
          <>
            <h1 className="titulo">Correo verificado</h1>
            <p className="bajada">Listo: tu cuenta ya está completa.</p>
            <Link className="boton boton--tinta" to="/entrar">Entrar<Icono nombre="flecha" /></Link>
          </>
        )}
        {resultado !== 'cargando' && resultado !== 'listo' && (
          <>
            <h1 className="titulo">No pudimos verificar</h1>
            <Aviso>{resultado}</Aviso>
            <p className="bajada">Si el enlace venció, entra y pide uno nuevo.</p>
            <Link className="boton" to="/entrar">Ir a entrar</Link>
          </>
        )}
      </div>
    </Marco>
  )
}

/**
 * API-14. Responde lo mismo exista o no la cuenta, y la pantalla tambien: decir "no tenemos ese
 * correo" le regalaria a cualquiera la lista de quien esta en ZASS!.
 */
export function RecuperarContrasena() {
  useTitulo('Recuperar contraseña')
  const [correo, setCorreo] = useState('')
  const [error, setError] = useState<string>()
  const [aviso, setAviso] = useState<string>()
  const [enviando, setEnviando] = useState(false)
  const [enviado, setEnviado] = useState(false)

  async function alEnviar(e: FormEvent) {
    e.preventDefault()
    setAviso(undefined)
    const limpio = correo.trim()
    if (limpio === '') { setError('Escribe tu correo.'); return }
    if (!/^[^@\s]+@[^@\s]+$/.test(limpio)) { setError('Ese correo no parece válido.'); return }
    setError(undefined)

    setEnviando(true)
    try {
      await pedirRecuperacion(limpio)
      setEnviado(true)
    } catch (e2) {
      setAviso(mensajeDe(e2))
    } finally {
      setEnviando(false)
    }
  }

  return (
    <Marco sobretitulo="Tu cuenta">
      {enviado ? (
        <div className="tarjeta">
          <h1 className="titulo">Revisa tu correo</h1>
          <p className="bajada">
            Si hay una cuenta con ese correo, te mandamos un enlace para elegir una contraseña nueva.
            Puede tardar unos minutos.
          </p>
          <Link className="boton" to="/entrar">Volver a entrar</Link>
        </div>
      ) : (
        <form className="tarjeta" onSubmit={(e) => void alEnviar(e)} noValidate>
          <h1 className="titulo">Recuperar contraseña</h1>
          <p className="bajada">Escribe tu correo y te mandamos un enlace.</p>
          {aviso && <Aviso>{aviso}</Aviso>}
          <Campo id="correo" etiqueta="Correo" tipo="email" autoComplete="email"
            valor={correo} alCambiar={setCorreo} error={error} />
          <button className="boton" type="submit" disabled={enviando}>
            {enviando ? 'Enviando…' : 'Mandarme el enlace'}
          </button>
        </form>
      )}
    </Marco>
  )
}

/** API-15. Se llega desde el enlace del correo: /restablecer-contrasena?token=... */
export function RestablecerContrasena() {
  useTitulo('Nueva contraseña')
  const [params] = useSearchParams()
  const token = params.get('token') ?? ''
  const [clave, setClave] = useState('')
  const [error, setError] = useState<string>()
  const [aviso, setAviso] = useState<string>()
  const [enviando, setEnviando] = useState(false)
  const [listo, setListo] = useState(false)

  async function alEnviar(e: FormEvent) {
    e.preventDefault()
    setAviso(undefined)
    if (clave.length < CONTRASENA_MINIMA) {
      setError(`Usa al menos ${CONTRASENA_MINIMA} caracteres.`)
      return
    }
    setError(undefined)

    setEnviando(true)
    try {
      await restablecerContrasena(token, clave)
      setListo(true)
    } catch (e2) {
      setAviso(mensajeDe(e2))
    } finally {
      setEnviando(false)
    }
  }

  if (token === '') {
    return (
      <Marco sobretitulo="Tu cuenta">
        <div className="tarjeta">
          <h1 className="titulo">Enlace incompleto</h1>
          <p className="bajada">Abre el enlace que te llegó al correo, o pide uno nuevo.</p>
          <Link className="boton" to="/recuperar-contrasena">Pedir un enlace nuevo</Link>
        </div>
      </Marco>
    )
  }

  return (
    <Marco sobretitulo="Tu cuenta">
      {listo ? (
        <div className="tarjeta">
          <h1 className="titulo">Contraseña cambiada</h1>
          <p className="bajada">Por seguridad cerramos tus sesiones abiertas. Entra con la nueva.</p>
          <Link className="boton boton--tinta" to="/entrar">Entrar<Icono nombre="flecha" /></Link>
        </div>
      ) : (
        <form className="tarjeta" onSubmit={(e) => void alEnviar(e)} noValidate>
          <h1 className="titulo">Elige una contraseña nueva</h1>
          {aviso && <Aviso>{aviso}</Aviso>}
          <Campo id="clave" etiqueta="Contraseña nueva" tipo="password" autoComplete="new-password"
            maximo={CONTRASENA_MAXIMA} valor={clave} alCambiar={setClave} error={error}
            ayuda={`Al menos ${CONTRASENA_MINIMA} caracteres.`} />
          <button className="boton" type="submit" disabled={enviando}>
            {enviando ? 'Guardando…' : 'Cambiar contraseña'}
          </button>
        </form>
      )}
    </Marco>
  )
}
