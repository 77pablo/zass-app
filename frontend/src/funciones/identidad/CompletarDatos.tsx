import { useState, type FormEvent } from 'react'
import { ErrorApi } from '../../api/cliente'
import { Aviso } from '../../componentes/Aviso'
import { Campo } from '../../componentes/Campo'
import { useSesion } from '../../sesion/contexto'
import { APELLIDO_MAXIMO, RAZON_SOCIAL_MAXIMA, revisarDatosDeCuenta } from './datosDeCuenta'
import { completarDatos, type TipoDeCuenta } from './servicio'
import '../../legal.css'

/*
 * ES-2 (decisión de Pablo, 20-nov-2026). Las cuentas creadas antes del registro completo no
 * tienen RUT, teléfono ni la casilla de mayoría de edad, y se les piden al entrar. El marco
 * muestra esta pantalla EN LUGAR de cualquier página mientras falten (no se puede saltar), y la
 * API responde 428 a todo lo que exige sesión hasta que se completen.
 *
 * Lo que sí se puede: cerrar sesión, y eliminar la cuenta (la API lo deja; irse no se condiciona
 * a entregar más datos).
 *
 * TEXTOS PROPUESTOS, pendientes de aprobación.
 */
export function CompletarDatos() {
  const { recargar, salir } = useSesion()
  const [tipo, setTipo] = useState<TipoDeCuenta>('particular')
  const [apellido, setApellido] = useState('')
  const [razonSocial, setRazonSocial] = useState('')
  const [rut, setRut] = useState('')
  const [telefono, setTelefono] = useState('')
  const [mayorDeEdad, setMayorDeEdad] = useState(false)
  const [campos, setCampos] = useState<Record<string, string>>({})
  const [aviso, setAviso] = useState<string>()
  const [enviando, setEnviando] = useState(false)

  const empresa = tipo === 'empresa'

  async function alEnviar(e: FormEvent) {
    e.preventDefault()
    setAviso(undefined)

    const malos = revisarDatosDeCuenta({ empresa, razonSocial, apellido, rut, telefono, mayorDeEdad })
    setCampos(malos)
    if (Object.keys(malos).length > 0) return

    setEnviando(true)
    try {
      await completarDatos({
        tipo,
        apellido: empresa ? undefined : apellido.trim(),
        razonSocial: empresa ? razonSocial.trim() : undefined,
        rut: rut.trim(),
        telefono: telefono.trim(),
        mayorDeEdad,
      })
      // La cuenta ya no tiene pendientes: al recargarla, el marco vuelve a mostrar la página.
      await recargar()
    } catch (e) {
      if (e instanceof ErrorApi) {
        setCampos(e.campos)
        if (Object.keys(e.campos).length === 0) setAviso(e.paraMostrar)
      } else {
        setAviso('Algo salió mal. Intenta de nuevo.')
      }
    } finally {
      setEnviando(false)
    }
  }

  return (
    <form className="tarjeta" onSubmit={alEnviar} noValidate>
      <h1 className="titulo">Completa los datos de tu cuenta</h1>
      <p className="bajada">
        Ahora pedimos a todas las cuentas su RUT y un teléfono, y que confirmen que son mayores de
        edad. Tu cuenta es de antes de este cambio: complétalos una vez para seguir usando ZASS!.
      </p>

      {aviso && <Aviso>{aviso}</Aviso>}

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

      <button className="boton" type="submit" disabled={enviando}>
        {enviando ? 'Guardando...' : 'Guardar y seguir'}
      </button>
      <button type="button" className="enlace" onClick={() => void salir()}>
        Cerrar sesión
      </button>
    </form>
  )
}
