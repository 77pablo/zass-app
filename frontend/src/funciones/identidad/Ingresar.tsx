import { useState, type FormEvent } from 'react'
import { ErrorApi } from '../../api/cliente'
import { iniciarSesion, type Sesion } from './servicio'
import { Campo } from '../../componentes/Campo'
import { Aviso } from '../../componentes/Aviso'

export function Ingresar({ alEntrar }: { alEntrar: (sesion: Sesion) => void }) {
  const [correo, setCorreo] = useState('')
  const [contrasena, setContrasena] = useState('')
  const [errorCorreo, setErrorCorreo] = useState<string>()
  const [errorContrasena, setErrorContrasena] = useState<string>()
  const [aviso, setAviso] = useState<string>()
  const [enviando, setEnviando] = useState(false)

  async function alEnviar(e: FormEvent) {
    e.preventDefault()
    setAviso(undefined)

    // Solo se comprueba que esten. NO se valida el largo de la contrasena:
    // decir "usa al menos 10" aqui le regala a cualquiera el minimo del sistema.
    // El backend toma la misma decision en ValidadorIniciarSesion.
    const faltaCorreo = correo.trim() === '' ? 'Escribe tu correo.' : undefined
    const faltaClave = contrasena === '' ? 'Escribe tu contraseña.' : undefined
    setErrorCorreo(faltaCorreo)
    setErrorContrasena(faltaClave)
    if (faltaCorreo || faltaClave) return

    setEnviando(true)
    try {
      alEntrar(await iniciarSesion(correo.trim(), contrasena))
    } catch (e) {
      // Se muestra el mensaje de la API tal cual: ya viene pensado para no
      // distinguir "contrasena mala" de "esa cuenta no existe".
      setAviso(e instanceof ErrorApi ? e.mensaje : 'Algo salió mal. Intenta de nuevo.')
    } finally {
      setEnviando(false)
    }
  }

  return (
    <form className="tarjeta" onSubmit={alEnviar} noValidate>
      <h1 className="titulo">Entrar</h1>
      <p className="bajada">Conecta con un toque</p>

      {aviso && <Aviso>{aviso}</Aviso>}

      <Campo
        id="correo" etiqueta="Correo" tipo="email" autoComplete="username"
        valor={correo} alCambiar={setCorreo} error={errorCorreo}
      />
      <Campo
        id="contrasena" etiqueta="Contraseña" tipo="password" autoComplete="current-password"
        valor={contrasena} alCambiar={setContrasena} error={errorContrasena}
      />

      <button className="boton" type="submit" disabled={enviando}>
        {enviando ? 'Entrando...' : 'Entrar'}
      </button>
    </form>
  )
}
