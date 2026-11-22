import { useState, type FormEvent } from 'react'
import { ErrorApi } from '../../api/cliente'
import { esDesafioMfa, iniciarSesion, segundoPaso, type Sesion } from './servicio'
import { Campo } from '../../componentes/Campo'
import { Aviso } from '../../componentes/Aviso'

export function Ingresar({ alEntrar }: { alEntrar: (sesion: Sesion) => void }) {
  const [correo, setCorreo] = useState('')
  const [contrasena, setContrasena] = useState('')
  const [errorCorreo, setErrorCorreo] = useState<string>()
  const [errorContrasena, setErrorContrasena] = useState<string>()
  const [aviso, setAviso] = useState<string>()
  const [enviando, setEnviando] = useState(false)
  // Verificacion en dos pasos: la contrasena era correcta y falta el codigo.
  const [desafio, setDesafio] = useState<string>()
  const [codigo, setCodigo] = useState('')
  const [errorCodigo, setErrorCodigo] = useState<string>()

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
      const r = await iniciarSesion(correo.trim(), contrasena)
      if (esDesafioMfa(r)) {
        setDesafio(r.desafio)
        setContrasena('')
      } else {
        alEntrar(r)
      }
    } catch (e) {
      // Se muestra el mensaje de la API tal cual: ya viene pensado para no
      // distinguir "contrasena mala" de "esa cuenta no existe".
      // Usa paraMostrar para incluir el código de petición en errores inesperados (DECISIÓN T4).
      setAviso(e instanceof ErrorApi ? e.paraMostrar : 'Algo salió mal. Intenta de nuevo.')
    } finally {
      setEnviando(false)
    }
  }

  async function alEnviarCodigo(e: FormEvent) {
    e.preventDefault()
    setAviso(undefined)
    const falta = codigo.trim() === '' ? 'Escribe el código.' : undefined
    setErrorCodigo(falta)
    if (falta || !desafio) return

    setEnviando(true)
    try {
      alEntrar(await segundoPaso(desafio, codigo.trim()))
    } catch (e) {
      setAviso(e instanceof ErrorApi ? e.paraMostrar : 'Algo salió mal. Intenta de nuevo.')
      setCodigo('')
    } finally {
      setEnviando(false)
    }
  }

  function volverAEmpezar() {
    setDesafio(undefined)
    setCodigo('')
    setAviso(undefined)
  }

  if (desafio) {
    return (
      <form className="tarjeta" onSubmit={alEnviarCodigo} noValidate>
        <h1 className="titulo">Verificación en dos pasos</h1>
        <p className="bajada">
          Abre tu app de autenticación y escribe el código de 6 dígitos de ZASS!. Si no tienes el
          teléfono a mano, puedes usar uno de tus códigos de respaldo.
        </p>

        {aviso && <Aviso>{aviso}</Aviso>}

        <Campo
          id="codigo-mfa" etiqueta="Código" autoComplete="one-time-code"
          valor={codigo} alCambiar={setCodigo} error={errorCodigo} maximo={20}
        />

        <button className="boton" type="submit" disabled={enviando}>
          {enviando ? 'Verificando...' : 'Verificar y entrar'}
        </button>
        <button type="button" className="enlace" onClick={volverAEmpezar}>
          Volver a escribir el correo y la contraseña
        </button>
      </form>
    )
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
