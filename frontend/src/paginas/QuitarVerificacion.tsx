import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import { Campo } from '../componentes/Campo'
import { quitarVerificacion } from '../funciones/interno/servicio'
import * as T from '../funciones/interno/textos'
import { useTitulo } from '../marco/useTitulo'

const MOTIVO_MINIMO = 10

/**
 * API-86 (revisión de seguridad, 25-nov-2026). Un administrador quita la verificación en dos pasos
 * de una cuenta: la persona perdió su teléfono y sus códigos, o alguien se la activó. La API no
 * deja hacerlo sobre la propia cuenta, cierra todo, deja la contraseña sin servir y avisa por
 * correo; aquí solo se pide el correo, el motivo y una confirmación.
 */
export function QuitarVerificacion() {
  useTitulo(T.SECCION_QUITAR_MFA)
  const [correo, setCorreo] = useState('')
  const [motivo, setMotivo] = useState('')
  const [errorMotivo, setErrorMotivo] = useState<string>()
  const [enviando, setEnviando] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [listo, setListo] = useState(false)

  async function alEnviar(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setListo(false)
    if (motivo.trim().length < MOTIVO_MINIMO) {
      setErrorMotivo(T.AYUDA_MOTIVO)
      return
    }
    setErrorMotivo(undefined)
    if (!window.confirm(T.confirmarQuitarMfa(correo.trim()))) return

    setEnviando(true)
    try {
      await quitarVerificacion(correo.trim(), motivo.trim())
      setListo(true)
      setCorreo('')
      setMotivo('')
    } catch (e2) {
      setError(e2 instanceof ErrorApi ? e2.paraMostrar : 'No se pudo completar la operación.')
    } finally {
      setEnviando(false)
    }
  }

  return (
    <div className="contenedor ficha-pagina">
      <h1 className="pagina-titulo">{T.SECCION_QUITAR_MFA}</h1>
      <p className="bloque-texto"><Link className="enlace" to="/interno">{T.VOLVER_AL_PANEL}</Link></p>
      <p className="bloque-texto">{T.INTRO_QUITAR_MFA}</p>
      {error && <p className="aviso aviso--malo" role="alert">{error}</p>}
      {listo && <p className="aviso" role="status">{T.MFA_QUITADA}</p>}
      <form className="bloque" onSubmit={(e) => void alEnviar(e)} noValidate>
        <Campo id="correo-cuenta" etiqueta={T.ETIQUETA_CORREO_CUENTA} tipo="email" autoComplete="off"
          valor={correo} alCambiar={setCorreo} />
        <Campo id="motivo" etiqueta={T.ETIQUETA_MOTIVO_QUITAR} maximo={500}
          valor={motivo} alCambiar={setMotivo} error={errorMotivo} ayuda={T.AYUDA_MOTIVO} />
        <button className="boton boton--tinta" type="submit" disabled={enviando || correo.trim() === ''}>
          {enviando ? T.QUITANDO_MFA : T.BOTON_QUITAR_MFA}
        </button>
      </form>
    </div>
  )
}
