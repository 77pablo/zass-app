import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { ErrorApi } from '../../api/cliente'
import { registrarse, type RolPublico, type TipoDeCuenta } from './servicio'
import { APELLIDO_MAXIMO, RAZON_SOCIAL_MAXIMA, revisarDatosDeCuenta } from './datosDeCuenta'
import { Campo } from '../../componentes/Campo'
import { Aviso } from '../../componentes/Aviso'
import '../../legal.css'

/** Los mismos de Limites, en Zass.Aplicacion/Identidad/Validadores.cs. */
const CONTRASENA_MINIMA = 10
const CONTRASENA_MAXIMA = 128
const CORREO_MAXIMO = 254
const NOMBRE_MAXIMO = 150

/**
 * El `codigo` con que API-08 responde (409) cuando el RUT ya tiene cuenta (decisión de Pablo,
 * 21-nov-2026). La cuenta no se crea y a la dueña se le manda un correo: aquí se muestra como
 * AVISO, no como error, con la salida a «Entrar» y a «Recuperar contraseña». El texto lo pone el
 * servidor, que es quien sabe que es el mismo siempre.
 */
const CODIGO_RUT_CON_CUENTA = 'rut_con_cuenta'

/*
 * Registro completo (decisión de Pablo, 20-nov-2026): además de correo y contraseña, se elige
 * si la cuenta es de un particular o de una empresa y se piden sus datos. NO se pide la fecha
 * de nacimiento: basta la casilla «soy mayor de 18» (minimización, Ley 21.719).
 *
 * TEXTOS PROPUESTOS, pendientes de aprobación de Pablo.
 */
export function Registrarse({
  alRegistrarse, rolInicial = 'buscador',
}: { alRegistrarse: () => void; rolInicial?: RolPublico }) {
  const [tipo, setTipo] = useState<TipoDeCuenta>('particular')
  const [nombre, setNombre] = useState('')
  const [apellido, setApellido] = useState('')
  const [razonSocial, setRazonSocial] = useState('')
  const [rut, setRut] = useState('')
  const [telefono, setTelefono] = useState('')
  const [correo, setCorreo] = useState('')
  const [contrasena, setContrasena] = useState('')
  const [mayorDeEdad, setMayorDeEdad] = useState(false)
  // No se pregunta: una sola cuenta sirve para buscar y para ofrecer. Quien llega desde «Para profesionales» parte
  // con la ficha lista; todos los demas empiezan buscando y activan «Ofrecer mis servicios» cuando quieran.
  const rol: RolPublico = rolInicial
  const [campos, setCampos] = useState<Record<string, string>>({})
  const [aviso, setAviso] = useState<string>()
  const [rutConCuenta, setRutConCuenta] = useState<string>()
  const [listo, setListo] = useState(false)
  const [enviando, setEnviando] = useState(false)

  const empresa = tipo === 'empresa'

  function revisar(): Record<string, string> {
    // Los datos de la cuenta se revisan con la MISMA función que «Completa tus datos» (ES-2).
    const malos = revisarDatosDeCuenta({ empresa, razonSocial, apellido, rut, telefono, mayorDeEdad })
    if (nombre.trim() === '') malos.nombre = empresa ? 'Escribe el nombre de la persona de contacto.' : 'Escribe tu nombre.'
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
    setRutConCuenta(undefined)

    const malos = revisar()
    setCampos(malos)
    if (Object.keys(malos).length > 0) return

    setEnviando(true)
    try {
      await registrarse({
        correo: correo.trim(),
        contrasena,
        nombre: nombre.trim(),
        rol,
        tipo,
        apellido: empresa ? undefined : apellido.trim(),
        razonSocial: empresa ? razonSocial.trim() : undefined,
        rut: rut.trim(),
        telefono: telefono.trim(),
        mayorDeEdad,
      })
      setListo(true)
      alRegistrarse()
    } catch (e) {
      if (e instanceof ErrorApi && e.datos.codigo === CODIGO_RUT_CON_CUENTA) {
        // No es un error que la persona pueda corregir en el formulario: es una salida.
        setRutConCuenta(e.mensaje)
      } else if (e instanceof ErrorApi) {
        // El backend valida de nuevo y puede ver algo que aqui no se vio.
        setCampos(e.campos)
        // El aviso general solo se muestra si el error no cabe en un campo.
        // Usa paraMostrar para incluir el código de petición en errores inesperados (DECISIÓN T4).
        if (Object.keys(e.campos).length === 0) setAviso(e.paraMostrar)
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
      <p className="bajada">Una sola cuenta para buscar y para ofrecer servicios.</p>

      {aviso && <Aviso>{aviso}</Aviso>}
      {rutConCuenta && (
        // Informativo, no rojo: la persona no hizo nada malo. role="status" lo lee el lector de
        // pantalla sin interrumpir como una alerta.
        <div className="aviso-linea" role="status">
          <p>{rutConCuenta}</p>
          <p>
            <Link to="/entrar">Entrar</Link>
            {' · '}
            <Link to="/recuperar-contrasena">Recuperar contraseña</Link>
          </p>
        </div>
      )}

      {rol === 'prestador' && (
        <p className="bajada">
          Vas a crear tu ficha para que te encuentren. Con la misma cuenta también puedes buscar.
        </p>
      )}

      <div className="campo">
        <span className="campo-etiqueta" id="tipo-etiqueta">La cuenta es de</span>
        <div className="segmentado" role="group" aria-labelledby="tipo-etiqueta">
          <button
            type="button" className="chip chip--radio" aria-pressed={!empresa}
            onClick={() => setTipo('particular')}
          >
            Un particular
          </button>
          <button
            type="button" className="chip chip--radio" aria-pressed={empresa}
            onClick={() => setTipo('empresa')}
          >
            Una empresa
          </button>
        </div>
      </div>

      {empresa && (
        <Campo
          id="razonsocial" etiqueta="Razón social" autoComplete="organization" maximo={RAZON_SOCIAL_MAXIMA}
          valor={razonSocial} alCambiar={setRazonSocial} error={campos.razonsocial}
        />
      )}
      <Campo
        id="rut" etiqueta={empresa ? 'RUT de la empresa' : 'RUT'} maximo={12}
        valor={rut} alCambiar={setRut} error={campos.rut}
        ayuda="Con dígito verificador, por ejemplo 12.345.678-5. No se muestra en tu ficha."
      />
      <Campo
        id="nombre" etiqueta={empresa ? 'Nombre de la persona de contacto' : 'Nombre'}
        autoComplete={empresa ? 'name' : 'given-name'} maximo={NOMBRE_MAXIMO}
        valor={nombre} alCambiar={setNombre} error={campos.nombre}
      />
      {!empresa && (
        <Campo
          id="apellido" etiqueta="Apellido" autoComplete="family-name" maximo={APELLIDO_MAXIMO}
          valor={apellido} alCambiar={setApellido} error={campos.apellido}
        />
      )}
      <Campo
        id="telefono" etiqueta="Teléfono" tipo="tel" autoComplete="tel" maximo={20}
        valor={telefono} alCambiar={setTelefono} error={campos.telefono}
        ayuda="Para contactarte si pasa algo con tu cuenta. No se muestra en tu ficha."
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

      <div className="campo">
        <label className="casilla" htmlFor="mayordeedad">
          <input
            id="mayordeedad" type="checkbox" checked={mayorDeEdad}
            onChange={(e) => setMayorDeEdad(e.target.checked)}
            aria-invalid={campos.mayordeedad ? true : undefined}
            aria-describedby={campos.mayordeedad ? 'mayordeedad-error' : undefined}
          />
          {empresa ? 'Soy mayor de 18 años y represento a esta empresa' : 'Soy mayor de 18 años'}
        </label>
        {campos.mayordeedad && <p className="campo-error" id="mayordeedad-error">{campos.mayordeedad}</p>}
      </div>

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
