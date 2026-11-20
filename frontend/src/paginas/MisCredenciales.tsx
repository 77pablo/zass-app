import { useState, type FormEvent } from 'react'
import { ErrorApi } from '../api/cliente'
import { Aviso } from '../componentes/Aviso'
import { Campo } from '../componentes/Campo'
import {
  ACEPTA_DOCUMENTO, TAMANO_MAXIMO,
  crearCredencial, eliminarCredencial, enviarCredencialARevision, quitarDocumento, subirDocumento,
  urlDeDocumento,
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
 * `Zass:Verificacion:PermitirSinDocumentos` esta apagada (RN-S2-01). Desde JQ-9 se puede subir,
 * asi que el texto dice que hacer en vez de explicar la regla. (Texto PROPUESTO: lo aprueba Pablo.)
 */
function motivoDelEnvio(e: unknown): string {
  if (e instanceof ErrorApi && e.estado === 422) {
    return 'Para enviarla a revisión, primero adjunta un documento que la respalde: PDF, JPG o PNG.'
  }
  return mensajeDe(e, 'No pudimos enviarla a revisión. Intenta de nuevo.')
}

/** «1,2 MB» o «340 KB»: para que la persona reconozca cuál subió. */
function peso(bytes: number): string {
  if (bytes >= 1024 * 1024) {
    return `${(bytes / 1024 / 1024).toLocaleString('es-CL', { maximumFractionDigits: 1 })} MB`
  }
  return `${Math.max(1, Math.round(bytes / 1024))} KB`
}

/** Hasta 5 por credencial, como el servidor (E-409). */
const MAXIMO_DOCUMENTOS = 5

/**
 * CU-05: declarar, adjuntar los documentos de respaldo (JQ-9), enviar a revision y eliminar.
 * Los documentos solo los ven su dueño y el Verificador que tiene la credencial tomada.
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
  // Igual que el envío: el error de un archivo va junto a SU credencial.
  const [subiendo, setSubiendo] = useState<string | null>(null)
  const [falloArchivo, setFalloArchivo] = useState<{ id: string; mensaje: string } | null>(null)

  async function adjuntar(id: string, archivo: File | undefined) {
    if (!archivo) return
    setFalloArchivo(null)
    // El servidor lo vuelve a mirar todo; esto solo evita esperar una subida que va a volver.
    if (archivo.size > TAMANO_MAXIMO) {
      setFalloArchivo({ id, mensaje: `El archivo pesa ${peso(archivo.size)} y el máximo es 10 MB.` })
      return
    }
    setSubiendo(id)
    try {
      await subirDocumento(id, archivo)
      alCambiar()
    } catch (e) {
      setFalloArchivo({ id, mensaje: mensajeDe(e, 'No pudimos subir el archivo. Intenta de nuevo.') })
    } finally {
      setSubiendo(null)
    }
  }

  async function quitar(id: string, documentoId: string) {
    setFalloArchivo(null)
    setSubiendo(id)
    try {
      await quitarDocumento(id, documentoId)
      alCambiar()
    } catch (e) {
      setFalloArchivo({ id, mensaje: mensajeDe(e, 'No pudimos quitar el archivo. Intenta de nuevo.') })
    } finally {
      setSubiendo(null)
    }
  }

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

                  {(c.respaldo ?? []).length > 0 && (
                    <ul className="respaldo" aria-label={`Documentos de ${c.nombre}`}>
                      {(c.respaldo ?? []).map((d) => (
                        <li key={d.id}>
                          <a href={urlDeDocumento(c.id, d.id)} target="_blank" rel="noopener noreferrer">
                            {d.nombre}
                          </a>
                          <small>{peso(d.tamanoBytes)}</small>
                          {c.estado !== 'Verificada' && (
                            <button type="button" className="editar" disabled={subiendo !== null}
                              onClick={() => void quitar(c.id, d.id)}>
                              Quitar<span className="solo-lector"> {d.nombre}</span>
                            </button>
                          )}
                        </li>
                      ))}
                    </ul>
                  )}

                  {c.estado !== 'Verificada' && (c.respaldo ?? []).length < MAXIMO_DOCUMENTOS && (
                    <label className={subiendo !== null ? 'editar adjuntar adjuntar--ocupado' : 'editar adjuntar'}>
                      <Icono nombre="mas" />
                      {subiendo === c.id ? 'Subiendo…' : 'Adjuntar documento'}
                      <span className="solo-lector"> de {c.nombre} (PDF, JPG o PNG, hasta 10 MB)</span>
                      <input
                        type="file" className="solo-lector" accept={ACEPTA_DOCUMENTO}
                        disabled={subiendo !== null}
                        onChange={(e) => { void adjuntar(c.id, e.target.files?.[0]); e.target.value = '' }}
                      />
                    </label>
                  )}
                  {falloArchivo?.id === c.id && (
                    <p className="campo-error" role="alert">{falloArchivo.mensaje}</p>
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
          Un borrador solo lo ves tú. Adjunta el documento que lo respalda y envíalo a revisión: lo
          revisamos a mano, uno por uno, y mientras tanto en tu ficha aparece como declarado por ti.
          Tus documentos solo los ves tú y la persona que lo revisa.
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
