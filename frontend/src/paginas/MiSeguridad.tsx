import { useEffect, useState, type FormEvent } from 'react'
import { Navigate } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import { Aviso } from '../componentes/Aviso'
import { Campo } from '../componentes/Campo'
import { CodigoQr } from '../componentes/tarjetas/CodigoQr'
import {
  confirmarAltaMfa, desactivarMfa, estadoMfa, iniciarAltaMfa, regenerarCodigosMfa,
  type AltaMfa, type EstadoMfa,
} from '../funciones/identidad/servicio'
import { PaginaCargando } from '../marco/PaginaCargando'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'
import { useSesion } from '../sesion/contexto'

export const RUTA_SEGURIDAD = '/mi-cuenta/seguridad'

function mensaje(e: unknown): string {
  return e instanceof ErrorApi ? e.paraMostrar : 'Algo salió mal. Intenta de nuevo.'
}

/**
 * «Mi cuenta» → Seguridad: activar o desactivar la verificación en dos pasos (API-80 a API-83), y
 * generar códigos de respaldo nuevos (API-84).
 * Obligatoria para las cuentas del panel interno (decisión de Pablo, 22-nov-2026); opcional
 * para el resto.
 */
export function MiSeguridad() {
  useTitulo('Seguridad de tu cuenta')
  useMeta({ noindex: true })
  const { estado: sesion } = useSesion()
  const [estado, setEstado] = useState<EstadoMfa>()
  const [error, setError] = useState<string>()
  const [alta, setAlta] = useState<AltaMfa>()
  const [codigos, setCodigos] = useState<string[]>()
  const [regenerados, setRegenerados] = useState(false)

  async function cargar() {
    try {
      setEstado(await estadoMfa())
    } catch (e) {
      setError(mensaje(e))
    }
  }

  useEffect(() => {
    if (sesion === 'dentro') void cargar()
  }, [sesion])

  if (sesion === 'fuera') return <Navigate to="/entrar" state={{ despues: RUTA_SEGURIDAD }} replace />
  if (!estado && !error) return <PaginaCargando titulo="Cargando…" />

  return (
    <div className="pagina-formulario">
      <div className="contenedor contenedor--angosto">
        <p className="sobretitulo sobretitulo--tinta">Mi cuenta</p>
        <div className="tarjeta">
          <h1 className="titulo">Verificación en dos pasos</h1>
          <p className="bajada">
            Además de tu contraseña, al entrar te pediremos un código de 6 dígitos que genera una app
            en tu teléfono (Google Authenticator, Microsoft Authenticator u otra). Así, aunque alguien
            sepa tu contraseña, no puede entrar a tu cuenta.
          </p>
          {error && <Aviso>{error}</Aviso>}

          {codigos ? (
            <CodigosDeRespaldo
              codigos={codigos} nuevos={regenerados}
              alTerminar={() => { setCodigos(undefined); setRegenerados(false); void cargar() }}
            />
          ) : estado?.activo ? (
            <Activa
              estado={estado} alDesactivar={() => void cargar()}
              alRegenerar={(c) => { setRegenerados(true); setCodigos(c) }}
            />
          ) : alta ? (
            <Confirmar alta={alta} alConfirmar={(c) => { setAlta(undefined); setRegenerados(false); setCodigos(c) }} />
          ) : estado ? (
            <>
              {estado.obligatorio && (
                <Aviso>
                  Tu cuenta tiene acceso al panel interno: para usarlo tienes que activar la
                  verificación en dos pasos.
                </Aviso>
              )}
              <p className="bloque-texto">Estado: <strong>desactivada</strong>.</p>
              <button
                type="button" className="boton"
                onClick={() => { setError(undefined); iniciarAltaMfa().then(setAlta, (e) => setError(mensaje(e))) }}
              >
                Activar la verificación en dos pasos
              </button>
            </>
          ) : null}
        </div>
      </div>
    </div>
  )
}

function Confirmar({ alta, alConfirmar }: { alta: AltaMfa; alConfirmar: (codigos: string[]) => void }) {
  const [contrasena, setContrasena] = useState('')
  const [codigo, setCodigo] = useState('')
  const [aviso, setAviso] = useState<string>()
  const [enviando, setEnviando] = useState(false)

  async function alEnviar(e: FormEvent) {
    e.preventDefault()
    if (contrasena === '' || codigo.trim() === '') { setAviso('Escribe tu contraseña y el código de 6 dígitos.'); return }
    setEnviando(true)
    setAviso(undefined)
    try {
      alConfirmar(await confirmarAltaMfa(contrasena, codigo.trim()))
    } catch (e) {
      setAviso(mensaje(e))
    } finally {
      setEnviando(false)
    }
  }

  return (
    <form onSubmit={alEnviar} noValidate>
      <p className="bloque-texto">
        <strong>1.</strong> Escanea este código QR con tu app de autenticación.
      </p>
      <CodigoQr texto={alta.uriOtpauth} alt="Código QR para la app de autenticación" />
      <p className="bloque-texto">
        ¿No puedes escanearlo? Escribe esta clave en la app: <code data-testid="clave-mfa">{alta.secreto}</code>
      </p>
      <p className="bloque-texto">
        <strong>2.</strong> Escribe tu contraseña y el código de 6 dígitos que aparece en la app.
      </p>
      {aviso && <Aviso>{aviso}</Aviso>}
      <Campo id="contrasena-alta" etiqueta="Contraseña" tipo="password" autoComplete="current-password" valor={contrasena} alCambiar={setContrasena} />
      <Campo id="codigo-alta" etiqueta="Código" autoComplete="one-time-code" valor={codigo} alCambiar={setCodigo} maximo={20} />
      <button className="boton" type="submit" disabled={enviando}>
        {enviando ? 'Verificando...' : 'Confirmar y activar'}
      </button>
    </form>
  )
}

function CodigosDeRespaldo({ codigos, nuevos, alTerminar }: { codigos: string[]; nuevos: boolean; alTerminar: () => void }) {
  const [copiados, setCopiados] = useState(false)

  async function copiar() {
    try {
      await navigator.clipboard.writeText(codigos.join('\n'))
      setCopiados(true)
    } catch {
      setCopiados(false)
    }
  }

  return (
    <div>
      <p className="bloque-texto">
        <strong>
          {nuevos
            ? 'Listo: estos son tus códigos de respaldo nuevos. Los anteriores ya no sirven.'
            : '¡Listo! La verificación en dos pasos quedó activa.'}
        </strong>
      </p>
      <p className="bloque-texto">
        Guarda estos códigos de respaldo en un lugar seguro. Cada uno sirve <strong>una sola vez</strong> para
        entrar si pierdes el teléfono. No los volveremos a mostrar.
      </p>
      <ul className="codigos-respaldo" aria-label="Códigos de respaldo">
        {codigos.map((c) => <li key={c}><code>{c}</code></li>)}
      </ul>
      <button type="button" className="boton boton--tinta" onClick={() => void copiar()}>
        {copiados ? 'Copiados' : 'Copiar los códigos'}
      </button>
      <button type="button" className="boton" onClick={alTerminar}>Ya los guardé</button>
    </div>
  )
}

function Activa({ estado, alDesactivar, alRegenerar }: {
  estado: EstadoMfa
  alDesactivar: () => void
  alRegenerar: (codigos: string[]) => void
}) {
  // Las dos acciones piden lo mismo (contraseña y código): un solo formulario, con lo que se eligió.
  const [accion, setAccion] = useState<'desactivar' | 'regenerar'>()
  const [contrasena, setContrasena] = useState('')
  const [codigo, setCodigo] = useState('')
  const [aviso, setAviso] = useState<string>()
  const [enviando, setEnviando] = useState(false)

  async function alEnviar(e: FormEvent) {
    e.preventDefault()
    if (contrasena === '' || codigo.trim() === '') {
      setAviso('Escribe tu contraseña y un código.')
      return
    }
    setEnviando(true)
    setAviso(undefined)
    try {
      if (accion === 'regenerar') alRegenerar(await regenerarCodigosMfa(contrasena, codigo.trim()))
      else {
        await desactivarMfa(contrasena, codigo.trim())
        alDesactivar()
      }
    } catch (e) {
      setAviso(mensaje(e))
    } finally {
      setEnviando(false)
    }
  }

  return (
    <>
      <p className="bloque-texto">Estado: <strong>activa</strong>.</p>
      <p className="bloque-texto">
        Te quedan {estado.codigosDeRespaldoRestantes} códigos de respaldo.
      </p>
      {estado.obligatorio && (
        <p className="bloque-texto">
          Tu cuenta tiene acceso al panel interno: si la desactivas, no podrás usarlo hasta volver a activarla.
        </p>
      )}
      {!accion ? (
        <>
          <button type="button" className="boton" onClick={() => setAccion('regenerar')}>
            Generar códigos de respaldo nuevos
          </button>
          <button type="button" className="boton boton--tinta" onClick={() => setAccion('desactivar')}>
            Desactivar
          </button>
        </>
      ) : (
        <form onSubmit={alEnviar} noValidate>
          {accion === 'regenerar' && (
            <p className="bloque-texto">
              Te daremos 10 códigos nuevos y los que tienes ahora dejarán de servir.
            </p>
          )}
          {aviso && <Aviso>{aviso}</Aviso>}
          <Campo id="contrasena-mfa" etiqueta="Contraseña" tipo="password" autoComplete="current-password" valor={contrasena} alCambiar={setContrasena} />
          <Campo id="codigo-desactivar" etiqueta="Código de la app o de respaldo" autoComplete="one-time-code" valor={codigo} alCambiar={setCodigo} maximo={20} />
          <button className="boton" type="submit" disabled={enviando}>
            {accion === 'regenerar'
              ? (enviando ? 'Generando...' : 'Generar los códigos nuevos')
              : (enviando ? 'Desactivando...' : 'Desactivar la verificación en dos pasos')}
          </button>
          <button type="button" className="boton boton--tinta" onClick={() => { setAccion(undefined); setAviso(undefined) }}>
            Volver
          </button>
        </form>
      )}
    </>
  )
}
