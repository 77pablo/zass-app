import { useState, type FormEvent } from 'react'
import { ErrorApi } from '../../api/cliente'
import { registrarse, type RolPublico } from './servicio'
import { Campo } from '../../componentes/Campo'
import { Aviso } from '../../componentes/Aviso'
import '../../legal.css'

/** Los mismos de Limites, en Zass.Aplicacion/Identidad/Validadores.cs. */
const CONTRASENA_MINIMA = 10
const CONTRASENA_MAXIMA = 128
const CORREO_MAXIMO = 254
const NOMBRE_MAXIMO = 150

export function Registrarse({
  alRegistrarse, rolInicial = 'buscador',
}: { alRegistrarse: () => void; rolInicial?: RolPublico }) {
  const [nombre, setNombre] = useState('')
  const [correo, setCorreo] = useState('')
  const [contrasena, setContrasena] = useState('')
  const [rol, setRol] = useState<RolPublico>(rolInicial)
  const [campos, setCampos] = useState<Record<string, string>>({})
  const [aviso, setAviso] = useState<string>()
  const [listo, setListo] = useState(false)
  const [enviando, setEnviando] = useState(false)

  function revisar(): Record<string, string> {
    const malos: Record<string, string> = {}
    if (nombre.trim() === '') malos.nombre = 'Escribe tu nombre.'
    if (correo.trim() === '') malos.correo = 'Escribe tu correo.'
    else if (!correo.includes('@')) malos.correo = 'Ese correo no parece válido.'
    // El minimo SI se dice aqui: sin saberlo no hay forma de cumplirlo. Al
    // ingresar se calla, porque ahi solo serviria para orientar a un atacante.
    if (contrasena.length < CONTRASENA_MINIMA)
      malos.contrasena = `Usa al menos ${CONTRASENA_MINIMA} caracteres.`
    return malos
  }

  async function alEnviar(e: FormEvent) {
    e.preventDefault()
    setAviso(undefined)

    const malos = revisar()
    setCampos(malos)
    if (Object.keys(malos).length > 0) return

    setEnviando(true)
    try {
      await registrarse(correo.trim(), contrasena, nombre.trim(), rol)
      setListo(true)
      alRegistrarse()
    } catch (e) {
      if (e instanceof ErrorApi) {
        // El backend valida de nuevo y puede ver algo que aqui no se vio.
        setCampos(e.campos)
        // El aviso general solo se muestra si el error no cabe en un campo.
        if (Object.keys(e.campos).length === 0) setAviso(e.mensaje)
      } else {
        setAviso('Algo salió mal. Intenta de nuevo.')
      }
    } finally {
      setEnviando(false)
    }
  }

  if (listo) {
    return (
      <div className="tarjeta">
        <h1 className="titulo">Revisa tu correo</h1>
        <p className="bajada">
          Le mandamos un enlace a <strong>{correo.trim()}</strong> para confirmar que
          es tuyo. Vence en 24 horas.
        </p>
      </div>
    )
  }

  return (
    <form className="tarjeta" onSubmit={alEnviar} noValidate>
      <h1 className="titulo">Crear cuenta</h1>
      <p className="bajada">Conecta con un toque</p>

      {aviso && <Aviso>{aviso}</Aviso>}

      <fieldset className="eleccion">
        <legend className="campo-etiqueta">En ZASS! vengo a…</legend>
        <label className={rol === 'buscador' ? 'opcion opcion--puesta' : 'opcion'}>
          <input
            type="radio" name="rol" value="buscador"
            checked={rol === 'buscador'}
            onChange={() => setRol('buscador')}
          />
          <span>
            <strong>Busco un servicio</strong>
            <small>Gratis, siempre</small>
          </span>
        </label>
        <label className={rol === 'prestador' ? 'opcion opcion--puesta' : 'opcion'}>
          <input
            type="radio" name="rol" value="prestador"
            checked={rol === 'prestador'}
            onChange={() => setRol('prestador')}
          />
          <span>
            <strong>Ofrezco un servicio</strong>
            <small>Aparece ante quien te busca cerca</small>
          </span>
        </label>
      </fieldset>

      <Campo
        id="nombre" etiqueta="Nombre" autoComplete="name" maximo={NOMBRE_MAXIMO}
        valor={nombre} alCambiar={setNombre} error={campos.nombre}
      />
      <Campo
        id="correo" etiqueta="Correo" tipo="email" autoComplete="email" maximo={CORREO_MAXIMO}
        valor={correo} alCambiar={setCorreo} error={campos.correo}
      />
      <Campo
        id="contrasena" etiqueta="Contraseña" tipo="password"
        autoComplete="new-password" maximo={CONTRASENA_MAXIMA}
        valor={contrasena} alCambiar={setContrasena} error={campos.contrasena}
        ayuda={`Al menos ${CONTRASENA_MINIMA} caracteres.`}
      />

      <p className="aceptacion">
        Al crear tu cuenta aceptas los{' '}
        <a href="/terminos" target="_blank" rel="noopener">Términos de uso</a> y la{' '}
        <a href="/privacidad" target="_blank" rel="noopener">Política de privacidad</a>.
      </p>

      <button className="boton" type="submit" disabled={enviando}>
        {enviando ? 'Creando...' : 'Crear cuenta'}
      </button>
    </form>
  )
}
