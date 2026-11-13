import { useState, type FormEvent } from 'react'
import { ErrorApi } from '../api/cliente'
import { Aviso } from '../componentes/Aviso'
import { Campo } from '../componentes/Campo'
import {
  crearCredencial, eliminarCredencial, enviarCredencialARevision,
  type MiCredencial, type TipoDeCredencial,
} from '../funciones/perfil/servicio'
import { Icono } from '../marco/Icono'
import { etiquetaDeTipo, hoyISO } from '../marco/texto'

const TIPOS: TipoDeCredencial[] = ['Titulo', 'Certificacion', 'Curso', 'Colegiatura']

/**
 * Lo que el dueño ve del estado INTERNO, dicho en palabras de persona. El publico nunca lo ve:
 * para el publico todo lo que no tiene sello es «Declarada por el usuario».
 */
function estadoEnPalabras(c: MiCredencial): { texto: string; sello: boolean } {
  switch (c.estado) {
    case 'Verificada': return { texto: 'Verificada', sello: true }
    case 'EnRevision': return { texto: 'En revisión', sello: false }
    case 'Rechazada': return { texto: 'No pudimos verificarla', sello: false }
    case 'Vencida': return { texto: 'Vencida', sello: false }
    default: return { texto: 'Borrador: solo tú la ves', sello: false }
  }
}

function mensajeDe(e: unknown, porDefecto: string): string {
  return e instanceof ErrorApi ? e.mensaje : porDefecto
}

/**
 * El 422 de API-27 solo puede ser uno: la credencial no tiene documento de respaldo y la bandera
 * `Zass:Verificacion:PermitirSinDocumentos` esta apagada (RN-S2-01). El servidor lo dice como
 * «Sube al menos un documento de respaldo», y subir documentos NO existe todavia (MOD-08):
 * repetir su mensaje tal cual manda a la persona a hacer algo que la interfaz no le ofrece. Por
 * eso este caso lleva su propio texto, y el resto si muestra el motivo del servidor.
 */
function motivoDelEnvio(e: unknown): string {
  if (e instanceof ErrorApi && e.estado === 422) {
    return 'Todavía no podemos revisarla: para eso hace falta un documento de respaldo y subir '
      + 'archivos aún no está disponible. Tu credencial sigue guardada como borrador.'
  }
  return mensajeDe(e, 'No pudimos enviarla a revisión. Intenta de nuevo.')
}

/**
 * CU-05, la parte que no necesita archivos: declarar, ver, enviar a revision y eliminar. Subir
 * documentos espera a MOD-08, pero enviar a revision NO: API-27 acepta una credencial sin
 * respaldo mientras la bandera de lanzamiento este activa, y sin este boton el sello
 * «Verificada» era inalcanzable y el filtro «Solo verificados» de la busqueda no devolvia a
 * nadie.
 */
export function MisCredenciales({
  credenciales, alCambiar,
}: { credenciales: MiCredencial[]; alCambiar: () => void }) {
  const [agregando, setAgregando] = useState(false)
  const [confirmando, setConfirmando] = useState<string | null>(null)
  const [aviso, setAviso] = useState<string>()
  const [trabajando, setTrabajando] = useState(false)
  const [enviando, setEnviando] = useState<string | null>(null)
  // El error va junto a la credencial que lo causo, no arriba: con varias en la lista, un aviso
  // suelto en la cabecera no dice de cual habla.
  const [falloEnvio, setFalloEnvio] = useState<{ id: string; mensaje: string } | null>(null)

  async function eliminar(id: string) {
    setTrabajando(true)
    setAviso(undefined)
    try {
      await eliminarCredencial(id)
      setConfirmando(null)
      alCambiar()
    } catch (e) {
      setAviso(mensajeDe(e, 'No pudimos eliminarla. Intenta de nuevo.'))
    } finally {
      setTrabajando(false)
    }
  }

  async function enviarARevision(id: string) {
    setEnviando(id)
    setFalloEnvio(null)
    try {
      await enviarCredencialARevision(id)
      // API-27 responde 204, sin la credencial: el estado nuevo se ve al volver a pedir la lista.
      alCambiar()
    } catch (e) {
      setFalloEnvio({ id, mensaje: motivoDelEnvio(e) })
    } finally {
      setEnviando(null)
    }
  }

  return (
    <section className="bloque">
      <div className="bloque-cabeza">
        <h2>Tus credenciales</h2>
        {!agregando && (
          <button type="button" className="editar" onClick={() => setAgregando(true)}>
            <Icono nombre="mas" />Agregar
          </button>
        )}
      </div>

      {aviso && <p className="aviso-linea" role="alert">{aviso}</p>}

      {agregando && (
        <FormularioDeCredencial
          alGuardar={() => { setAgregando(false); alCambiar() }}
          alCancelar={() => setAgregando(false)}
        />
      )}

      {credenciales.length > 0 ? (
        <ul className="credenciales">
          {credenciales.map((c) => {
            const estado = estadoEnPalabras(c)
            return (
              <li key={c.id}>
                <Icono nombre="sello" />
                <div>
                  <strong>{c.nombre}</strong>
                  <small>
                    {[etiquetaDeTipo(c.tipo), c.entidad, c.obtenidaEn.slice(0, 4)].filter(Boolean).join(' · ')}
                  </small>
                  <span className={estado.sello ? 'sello' : 'dato'}>{estado.texto}</span>
                  {c.estado === 'Borrador' && (
                    <button
                      type="button" className="editar" disabled={enviando !== null}
                      onClick={() => void enviarARevision(c.id)}
                    >
                      <Icono nombre="sello" />
                      {enviando === c.id ? 'Enviando…' : 'Enviar a revisión'}
                      <span className="solo-lector"> {c.nombre}</span>
                    </button>
                  )}
                  {falloEnvio?.id === c.id && (
                    <p className="campo-error" role="alert">{falloEnvio.mensaje}</p>
                  )}
                </div>
                {confirmando === c.id ? (
                  <span className="credencial-confirmar">
                    <span>{c.estado === 'Verificada' ? 'Perderás el sello. ¿Eliminar?' : '¿Eliminar?'}</span>
                    <button type="button" className="editar" disabled={trabajando} onClick={() => void eliminar(c.id)}>
                      Sí, eliminar<span className="solo-lector"> {c.nombre}</span>
                    </button>
                    <button type="button" className="editar" onClick={() => setConfirmando(null)}>No</button>
                  </span>
                ) : (
                  <button type="button" className="editar" onClick={() => setConfirmando(c.id)}>
                    <Icono nombre="cerrar" />Eliminar<span className="solo-lector"> {c.nombre}</span>
                  </button>
                )}
              </li>
            )
          })}
        </ul>
      ) : (
        !agregando && (
          <div className="invitacion">
            <p>Tus títulos y certificaciones le dan confianza a quien te busca.</p>
            <button type="button" className="boton boton--chico" onClick={() => setAgregando(true)}>
              <Icono nombre="mas" />Agregar una credencial
            </button>
          </div>
        )
      )}

      {credenciales.some((c) => c.estado === 'Borrador') && (
        <p className="letra-chica">
          Un borrador solo lo ves tú. Envíalo a revisión y lo revisamos a mano, una por una;
          mientras lo hacemos, en tu ficha aparece como declarado por ti. Subir documentos
          todavía no está disponible.
        </p>
      )}
    </section>
  )
}

function FormularioDeCredencial({
  alGuardar, alCancelar,
}: { alGuardar: () => void; alCancelar: () => void }) {
  const [tipo, setTipo] = useState<TipoDeCredencial>('Titulo')
  const [nombre, setNombre] = useState('')
  const [entidad, setEntidad] = useState('')
  const [obtenidaEn, setObtenidaEn] = useState('')
  const [venceEn, setVenceEn] = useState('')
  const [errores, setErrores] = useState<Record<string, string>>({})
  const [aviso, setAviso] = useState<string>()
  const [guardando, setGuardando] = useState(false)

  const hoy = hoyISO()

  function revisar(): Record<string, string> {
    const malos: Record<string, string> = {}
    const n = nombre.trim()
    if (n.length < 3 || n.length > 150) malos.nombre = 'Escribe el nombre, entre 3 y 150 caracteres.'
    if (entidad.trim() !== '' && (entidad.trim().length < 3 || entidad.trim().length > 200))
      malos.entidadNombre = 'El nombre de la entidad debe tener entre 3 y 200 caracteres.'
    if (obtenidaEn === '') malos.obtenidaEn = 'Indica cuándo la obtuviste.'
    else if (obtenidaEn > hoy) malos.obtenidaEn = 'La fecha no puede ser futura.'
    if (venceEn !== '' && obtenidaEn !== '' && venceEn <= obtenidaEn)
      malos.venceEn = 'El vencimiento tiene que ser posterior a la fecha de obtención.'
    return malos
  }

  async function enviar(e: FormEvent) {
    e.preventDefault()
    setAviso(undefined)
    const malos = revisar()
    setErrores(malos)
    if (Object.keys(malos).length > 0) return

    setGuardando(true)
    try {
      await crearCredencial({
        tipo, nombre: nombre.trim(),
        entidadNombre: entidad.trim() === '' ? null : entidad.trim(),
        obtenidaEn, venceEn: venceEn === '' ? null : venceEn,
      })
      alGuardar()
    } catch (err) {
      if (err instanceof ErrorApi && Object.keys(err.campos).length > 0) setErrores(err.campos)
      setAviso(mensajeDe(err, 'No pudimos guardarla. Intenta de nuevo.'))
    } finally {
      setGuardando(false)
    }
  }

  return (
    <form className="editor" onSubmit={(e) => void enviar(e)} noValidate>
      {aviso && <Aviso>{aviso}</Aviso>}

      <div className="campo">
        <label className="campo-etiqueta" htmlFor="c-tipo">Qué es</label>
        <select id="c-tipo" className="campo-entrada" value={tipo} onChange={(e) => setTipo(e.target.value as TipoDeCredencial)}>
          {TIPOS.map((t) => <option key={t} value={t}>{etiquetaDeTipo(t)}</option>)}
        </select>
      </div>

      <Campo id="c-nombre" etiqueta="Nombre de la credencial" valor={nombre} alCambiar={setNombre}
        error={errores.nombre} maximo={150} ayuda="Por ejemplo: Abogado, o Curso de tasación" />
      <Campo id="c-entidad" etiqueta="Quién la otorgó (opcional)" valor={entidad} alCambiar={setEntidad}
        error={errores.entidadnombre ?? errores.entidadNombre} maximo={200} />

      <div className="editor-rejilla">
        <div className="campo">
          <label className="campo-etiqueta" htmlFor="c-obtenida">Cuándo la obtuviste</label>
          <input id="c-obtenida" type="date" max={hoy} value={obtenidaEn} onChange={(e) => setObtenidaEn(e.target.value)}
            className={errores.obtenidaEn ? 'campo-entrada campo-entrada--mal' : 'campo-entrada'}
            aria-invalid={errores.obtenidaEn ? true : undefined} aria-describedby={errores.obtenidaEn ? 'c-obtenida-error' : undefined} />
          {errores.obtenidaEn && <p className="campo-error" id="c-obtenida-error">{errores.obtenidaEn}</p>}
        </div>
        <div className="campo">
          <label className="campo-etiqueta" htmlFor="c-vence">Cuándo vence (opcional)</label>
          <input id="c-vence" type="date" value={venceEn} onChange={(e) => setVenceEn(e.target.value)}
            className={errores.venceEn ? 'campo-entrada campo-entrada--mal' : 'campo-entrada'}
            aria-invalid={errores.venceEn ? true : undefined} aria-describedby={errores.venceEn ? 'c-vence-error' : undefined} />
          {errores.venceEn && <p className="campo-error" id="c-vence-error">{errores.venceEn}</p>}
        </div>
      </div>

      <div className="editor-acciones">
        <button className="boton boton--tinta" type="submit" disabled={guardando}>
          {guardando ? 'Guardando…' : 'Guardar credencial'}
        </button>
        <button className="boton" type="button" onClick={alCancelar} disabled={guardando}>Cancelar</button>
      </div>
    </form>
  )
}
