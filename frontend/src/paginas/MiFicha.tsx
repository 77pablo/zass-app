import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link, Navigate } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import { serviciosDeRubro, type Rubro, type ServicioDeCatalogo } from '../api/catalogo'
import { useCatalogo } from '../api/useCargado'
import { Campo } from '../componentes/Campo'
import {
  despublicar, guardarDatos, guardarServicios, miCobertura, miPerfil,
  misServicios, miSuscripcion, publicar, misCredenciales,
  type MiCredencial, type MiPerfil, type ServicioOfrecido, type ServicioParaGuardar, type Suscripcion, type ZonaCubierta,
} from '../funciones/perfil/servicio'
import { erroresDelServidor, LIMITES, validarDatos, type ErroresDelFormulario } from '../funciones/perfil/validacion'
import { Icono } from '../marco/Icono'
import { Monograma } from '../marco/Monograma'
import { capitalizar, duracion, pesos } from '../marco/texto'
import { useTitulo } from '../marco/useTitulo'
import { useSesion } from '../sesion/contexto'
import { EditorDeZonas } from './EditorDeZonas'
import { MisCredenciales } from './MisCredenciales'

interface Todo {
  perfil: MiPerfil
  servicios: ServicioOfrecido[]
  cobertura: ZonaCubierta[]
  suscripcion: Suscripcion | null
  credenciales: MiCredencial[]
}

type Seccion = 'datos' | 'servicios' | 'zonas'

function mensajeDe(e: unknown, porDefecto: string): string {
  return e instanceof ErrorApi ? e.mensaje : porDefecto
}

/**
 * CU-03. Editar tu perfil es editar tu propia ficha: la ves como la ve el publico y cada
 * bloque se edita en su lugar. Lo que falta para publicar es una guia que se va completando,
 * no una lista de errores.
 */
export function MiFicha() {
  const { estado, usuario } = useSesion()
  const catalogo = useCatalogo()
  useTitulo('Mi ficha')

  const [todo, setTodo] = useState<Todo | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [recarga, setRecarga] = useState(0)
  const [editando, setEditando] = useState<Seccion | null>(null)
  const [avisoAccion, setAvisoAccion] = useState<string | null>(null)
  const [trabajando, setTrabajando] = useState(false)

  const esPrestador = usuario?.roles.includes('prestador') ?? false

  useEffect(() => {
    if (estado !== 'dentro' || !esPrestador) return
    let vigente = true
    Promise.all([
      miPerfil(), misServicios(), miCobertura(),
      // La suscripcion todavia no existe antes de publicar por primera vez: un 404 aqui es normal.
      miSuscripcion().catch(() => null),
      misCredenciales(),
    ])
      .then(([perfil, servicios, cobertura, suscripcion, credenciales]) => {
        if (vigente) { setTodo({ perfil, servicios, cobertura, suscripcion, credenciales }); setError(null) }
      })
      .catch((e: unknown) => {
        if (vigente) setError(mensajeDe(e, 'No pudimos cargar tu ficha.'))
      })
    return () => { vigente = false }
  }, [estado, esPrestador, recarga])

  const recargar = useCallback(() => setRecarga((n) => n + 1), [])

  if (estado === 'cargando') {
    return <div className="contenedor ficha-pagina"><p className="cargando-linea" role="status">Cargando…</p></div>
  }
  if (estado === 'fuera') return <Navigate to="/entrar" replace state={{ despues: '/mi-ficha' }} />

  if (!esPrestador) {
    return (
      <section className="contenedor no-encontrada">
        <p className="sobretitulo sobretitulo--tinta">Mi ficha</p>
        <h1 className="pagina-titulo">Tu cuenta es de quien busca.</h1>
        <p className="bajada">
          La ficha es para quienes ofrecen un servicio. Si eres profesional, crea una cuenta de
          profesional con otro correo.
        </p>
        <p className="seccion-cierre">
          <Link className="boton boton--amarillo" to="/buscar">Buscar a alguien<Icono nombre="flecha" /></Link>
        </p>
      </section>
    )
  }

  if (error) {
    return (
      <div className="contenedor ficha-pagina">
        <p className="aviso-linea" role="alert">
          {error}{' '}
          <button type="button" className="enlace" onClick={() => { setError(null); recargar() }}>Reintentar</button>
        </p>
      </div>
    )
  }
  if (todo === null || catalogo.datos === null) {
    return <div className="contenedor ficha-pagina"><p className="cargando-linea" role="status">Cargando tu ficha…</p></div>
  }

  const { perfil, servicios, cobertura, suscripcion, credenciales } = todo
  const { rubros, zonas } = catalogo.datos
  const rubro = rubros.find((r) => r.id === perfil.rubroId)
  const activos = servicios.filter((s) => s.activo)
  const falta = perfil.faltaParaPublicar
  const publicada = perfil.estado === 'Publicado'

  async function cambiarPublicacion() {
    setTrabajando(true)
    setAvisoAccion(null)
    try {
      if (publicada) await despublicar()
      else await publicar()
      recargar()
    } catch (e) {
      setAvisoAccion(mensajeDe(e, 'No pudimos cambiar el estado de tu ficha.'))
    } finally {
      setTrabajando(false)
    }
  }

  const alGuardar = () => { setEditando(null); recargar() }

  return (
    <div className="contenedor ficha-pagina">
      <header className="mi-cabecera">
        <div className="persona">
          <Monograma nombre={perfil.nombrePublico} tamano="g" />
          <div>
            <p className="sobretitulo sobretitulo--tinta">Mi ficha</p>
            <h1>{perfil.nombrePublico}</h1>
            <p className="persona-detalle">
              {[rubro && capitalizar(rubro.nombre), perfil.zonaPrincipal].filter(Boolean).join(' · ') || 'Aún sin oficio ni barrio'}
            </p>
            <p className="persona-detalle"><Link to="/mi-agenda">Mi agenda</Link> · <Link to="/mis-citas">Mis citas</Link></p>
            <div className="fila-etiquetas">
              <span className={publicada ? 'sello' : 'dato'}>
                {publicada ? 'Publicada: la ven en las búsquedas' : perfil.estado === 'Despublicado' ? 'En pausa: nadie la ve' : 'Borrador: nadie la ve todavía'}
              </span>
              {suscripcion?.enPrueba && suscripcion.vigente && (
                <span className="dato">Prueba · {suscripcion.diasRestantes} días</span>
              )}
            </div>
          </div>
        </div>

        <div className="mi-acciones">
          <button
            type="button" className={publicada ? 'boton' : 'boton boton--tinta'}
            disabled={trabajando || (!publicada && falta.length > 0)}
            onClick={() => void cambiarPublicacion()}
          >
            {trabajando ? 'Un momento…' : publicada ? 'Pausar mi ficha' : 'Publicar mi ficha'}
          </button>
          {publicada && (
            <Link className="enlace" to={`/prestadores/${perfil.slug}`}>Ver como la ve el público</Link>
          )}
        </div>
      </header>

      {avisoAccion && <p className="aviso-linea" role="alert">{avisoAccion}</p>}

      {!publicada && (
        <section className="guia" aria-label="Lo que falta para publicar">
          {falta.length > 0 ? (
            <>
              <h2>Ya casi. Te falta:</h2>
              <ul>{falta.map((f) => <li key={f}><Icono nombre="mas" />{capitalizar(f)}</li>)}</ul>
            </>
          ) : (
            <>
              <h2>Tu ficha está lista.</h2>
              <p>Cuando la publiques, empieza tu período de prueba y la verán en las búsquedas de tus comunas.</p>
            </>
          )}
        </section>
      )}

      <section className="bloque">
        <div className="bloque-cabeza">
          <h2>Sobre ti</h2>
          {editando !== 'datos' && (
            <button type="button" className="editar" onClick={() => setEditando('datos')}>
              <Icono nombre="lapiz" />Editar
            </button>
          )}
        </div>
        {editando === 'datos' ? (
          <EditorDeDatos perfil={perfil} rubros={rubros} alGuardar={alGuardar} alCancelar={() => setEditando(null)} />
        ) : (
          <>
            {perfil.descripcion
              ? <p className="bloque-texto texto-largo">{perfil.descripcion}</p>
              : <Invitacion texto="Cuéntale a tu futuro cliente quién eres y cómo trabajas." accion="Escribir mi presentación" alPulsar={() => setEditando('datos')} />}
            <ul className="contacto-lista">
              {[perfil.whatsapp && `WhatsApp ${perfil.whatsapp}`, perfil.telefonoPublico && `Teléfono ${perfil.telefonoPublico}`,
                perfil.correoPublico, perfil.sitioWeb].filter(Boolean).map((t) => <li key={t}>{t}</li>)}
            </ul>
          </>
        )}
      </section>

      <section className="bloque">
        <div className="bloque-cabeza">
          <h2>Qué ofreces</h2>
          {editando !== 'servicios' && perfil.rubroId !== null && (
            <button type="button" className="editar" onClick={() => setEditando('servicios')}>
              <Icono nombre="lapiz" />Editar
            </button>
          )}
        </div>
        {perfil.rubroId === null ? (
          <Invitacion texto="Primero elige tu oficio en «Sobre ti»; así te sugerimos servicios." accion="Elegir mi oficio" alPulsar={() => setEditando('datos')} />
        ) : editando === 'servicios' ? (
          <EditorDeServicios
            rubroId={perfil.rubroId} actuales={activos}
            alGuardar={alGuardar} alCancelar={() => setEditando(null)}
          />
        ) : activos.length > 0 ? (
          <ul className="servicios">
            {activos.map((s) => (
              <li key={s.id} className="servicio">
                <strong>{s.nombre}</strong>
                <span>{duracion(s.duracionMinutos)}{s.precioReferencia !== null && ` · desde ${pesos(s.precioReferencia)}`}</span>
              </li>
            ))}
          </ul>
        ) : (
          <Invitacion texto="Agrega al menos un servicio para poder publicar." accion="Agregar servicios" alPulsar={() => setEditando('servicios')} />
        )}
      </section>

      <section className="bloque">
        <div className="bloque-cabeza">
          <h2>Dónde atiendes</h2>
          {editando !== 'zonas' && (
            <button type="button" className="editar" onClick={() => setEditando('zonas')}>
              <Icono nombre="lapiz" />Editar
            </button>
          )}
        </div>
        {editando === 'zonas' ? (
          <EditorDeZonas zonas={zonas} actuales={cobertura} alGuardar={alGuardar} alCancelar={() => setEditando(null)} />
        ) : cobertura.length > 0 ? (
          <ul className="zonas">
            {cobertura.map((z) => (
              <li key={z.zonaId} className={z.esPrincipal ? 'zona zona--principal' : 'zona'}>
                <Icono nombre="pin" />{z.nombre}
              </li>
            ))}
          </ul>
        ) : (
          <Invitacion texto="Elige las comunas donde atiendes y cuál es la principal." accion="Elegir mis comunas" alPulsar={() => setEditando('zonas')} />
        )}
      </section>

      <MisCredenciales credenciales={credenciales} alCambiar={recargar} />

      <p className="letra-chica">
        Por ahora no puedes subir tu foto, tus títulos ni tu dirección.
        Mientras tanto, tu cara en la ficha son tus iniciales.
      </p>
    </div>
  )
}

function Invitacion({ texto, accion, alPulsar }: { texto: string; accion: string; alPulsar: () => void }) {
  return (
    <div className="invitacion">
      <p>{texto}</p>
      <button type="button" className="boton boton--chico" onClick={alPulsar}><Icono nombre="mas" />{accion}</button>
    </div>
  )
}

/* ------------------------------------------------------------------ Sobre ti */

function EditorDeDatos({
  perfil, rubros, alGuardar, alCancelar,
}: { perfil: MiPerfil; rubros: Rubro[]; alGuardar: () => void; alCancelar: () => void }) {
  const [d, setD] = useState({
    nombrePublico: perfil.nombrePublico,
    descripcion: perfil.descripcion ?? '',
    rubroId: perfil.rubroId === null ? '' : String(perfil.rubroId),
    telefonoPublico: perfil.telefonoPublico ?? '',
    correoPublico: perfil.correoPublico ?? '',
    whatsapp: perfil.whatsapp ?? '',
    sitioWeb: perfil.sitioWeb ?? '',
  })
  const [errores, setErrores] = useState<ErroresDelFormulario>({})
  const [aviso, setAviso] = useState<string>()
  const [guardando, setGuardando] = useState(false)

  const poner = (campo: keyof typeof d) => (v: string) => setD((a) => ({ ...a, [campo]: v }))

  async function enviar(e: FormEvent) {
    e.preventDefault()
    const malos = validarDatos(d)
    setErrores(malos)
    setAviso(undefined)
    if (Object.keys(malos).length > 0) return

    setGuardando(true)
    try {
      await guardarDatos({ ...d, rubroId: d.rubroId === '' ? null : Number(d.rubroId) }, perfil.version)
      alGuardar()
    } catch (err) {
      if (err instanceof ErrorApi && Object.keys(err.campos).length > 0) setErrores(erroresDelServidor(err.campos))
      setAviso(mensajeDe(err, 'No pudimos guardar. Intenta de nuevo.'))
    } finally {
      setGuardando(false)
    }
  }

  return (
    <form className="editor" onSubmit={(e) => void enviar(e)} noValidate>
      {aviso && <p className="aviso-linea" role="alert">{aviso}</p>}
      <Campo id="e-nombre" etiqueta="Tu nombre, como te van a ver" valor={d.nombrePublico} alCambiar={poner('nombrePublico')} error={errores.nombrePublico} maximo={LIMITES.nombrePublico} />

      <div className="campo">
        <label className="campo-etiqueta" htmlFor="e-rubro">Tu oficio</label>
        <select id="e-rubro" className="campo-entrada" value={d.rubroId} onChange={(e) => poner('rubroId')(e.target.value)}>
          <option value="">Elige tu oficio</option>
          {rubros.map((r) => <option key={r.id} value={r.id}>{capitalizar(r.nombre)}</option>)}
        </select>
      </div>

      <div className="campo">
        <label className="campo-etiqueta" htmlFor="e-desc">Sobre ti</label>
        <textarea
          id="e-desc" className={errores.descripcion ? 'campo-entrada campo-entrada--mal' : 'campo-entrada'} rows={7}
          value={d.descripcion} onChange={(e) => poner('descripcion')(e.target.value)}
          aria-describedby="e-desc-cuenta"
        />
        <p className={d.descripcion.length > LIMITES.descripcion ? 'campo-error' : 'campo-ayuda'} id="e-desc-cuenta">
          {errores.descripcion ?? `${d.descripcion.length} de ${LIMITES.descripcion}`}
        </p>
      </div>

      <div className="editor-rejilla">
        <Campo id="e-whatsapp" etiqueta="WhatsApp" valor={d.whatsapp} alCambiar={poner('whatsapp')} error={errores.whatsapp} tipo="tel" autoComplete="off" />
        <Campo id="e-tel" etiqueta="Teléfono" valor={d.telefonoPublico} alCambiar={poner('telefonoPublico')} error={errores.telefonoPublico} tipo="tel" autoComplete="off" />
        <Campo id="e-correo" etiqueta="Correo para contacto" valor={d.correoPublico} alCambiar={poner('correoPublico')} error={errores.correoPublico} tipo="email" autoComplete="off" />
        <Campo id="e-sitio" etiqueta="Sitio web" valor={d.sitioWeb} alCambiar={poner('sitioWeb')} error={errores.sitioWeb} tipo="url" autoComplete="off" ayuda="Empieza con https://" />
      </div>
      <p className="campo-ayuda">Lo que dejes vacío no se muestra. Cada dato que pongas lo verá cualquiera.</p>

      <div className="editor-acciones">
        <button className="boton boton--tinta" type="submit" disabled={guardando}>{guardando ? 'Guardando…' : 'Guardar'}</button>
        <button className="boton" type="button" onClick={alCancelar} disabled={guardando}>Cancelar</button>
      </div>
    </form>
  )
}

/* ---------------------------------------------------------------- Servicios */

interface Fila {
  clave: string
  servicioId?: number
  nombre: string
  nuevo: boolean
  duracion: string
  precio: string
  modo: 'automatico' | 'manual'
}

function EditorDeServicios({
  rubroId, actuales, alGuardar, alCancelar,
}: { rubroId: number; actuales: ServicioOfrecido[]; alGuardar: () => void; alCancelar: () => void }) {
  const [filas, setFilas] = useState<Fila[]>(() => actuales.map((s) => ({
    clave: s.id, servicioId: s.servicioId, nombre: s.nombre, nuevo: false,
    duracion: String(s.duracionMinutos), precio: s.precioReferencia === null ? '' : String(s.precioReferencia),
    modo: s.modoAgenda === 'Manual' ? 'manual' : 'automatico',
  })))
  const [sugeridos, setSugeridos] = useState<ServicioDeCatalogo[]>([])
  const [otro, setOtro] = useState('')
  const [aviso, setAviso] = useState<string>()
  const [guardando, setGuardando] = useState(false)

  useEffect(() => {
    let vigente = true
    serviciosDeRubro(rubroId).then((s) => { if (vigente) setSugeridos(s) }).catch(() => {})
    return () => { vigente = false }
  }, [rubroId])

  const sinAgregar = sugeridos.filter((s) => !filas.some((f) => f.servicioId === s.id))

  const agregarDelCatalogo = (s: ServicioDeCatalogo) =>
    setFilas((a) => [...a, { clave: `c${s.id}`, servicioId: s.id, nombre: s.nombre, nuevo: false, duracion: '60', precio: '', modo: 'automatico' }])

  function agregarPropio() {
    const nombre = otro.trim()
    if (nombre === '') return
    setFilas((a) => [...a, { clave: `n${Date.now()}`, nombre, nuevo: true, duracion: '60', precio: '', modo: 'automatico' }])
    setOtro('')
  }

  const cambiar = (clave: string, campo: 'duracion' | 'precio', v: string) =>
    setFilas((a) => a.map((f) => (f.clave === clave ? { ...f, [campo]: v } : f)))

  async function enviar(e: FormEvent) {
    e.preventDefault()
    setAviso(undefined)
    const lista: ServicioParaGuardar[] = []
    for (const f of filas) {
      const min = Number(f.duracion)
      if (!Number.isInteger(min) || min < 5 || min > 600) {
        setAviso(`La duración de «${f.nombre}» tiene que estar entre 5 y 600 minutos.`)
        return
      }
      const precio = f.precio.trim() === '' ? null : Number(f.precio)
      if (precio !== null && (!Number.isInteger(precio) || precio < 0)) {
        setAviso(`El precio de «${f.nombre}» tiene que ser un número entero en pesos.`)
        return
      }
      lista.push({
        ...(f.nuevo ? { nombreNuevo: f.nombre } : { servicioId: f.servicioId }),
        duracionMinutos: min, modoAgenda: f.modo, precioReferencia: precio,
      })
    }
    setGuardando(true)
    try {
      await guardarServicios(lista)
      alGuardar()
    } catch (err) {
      setAviso(mensajeDe(err, 'No pudimos guardar tus servicios.'))
    } finally {
      setGuardando(false)
    }
  }

  return (
    <form className="editor" onSubmit={(e) => void enviar(e)} noValidate>
      {aviso && <p className="aviso-linea" role="alert">{aviso}</p>}

      {filas.length === 0 && <p className="bloque-vacio">Todavía no hay servicios. Agrega uno de abajo.</p>}
      <ul className="editor-servicios">
        {filas.map((f) => (
          <li key={f.clave} className="editor-servicio">
            <strong>{f.nombre}</strong>
            <label>Minutos
              <input className="campo-entrada" inputMode="numeric" value={f.duracion} onChange={(e) => cambiar(f.clave, 'duracion', e.target.value)} />
            </label>
            <label>Desde (pesos)
              <input className="campo-entrada" inputMode="numeric" value={f.precio} placeholder="opcional" onChange={(e) => cambiar(f.clave, 'precio', e.target.value)} />
            </label>
            <button type="button" className="editar" onClick={() => setFilas((a) => a.filter((x) => x.clave !== f.clave))}>
              <Icono nombre="cerrar" />Quitar<span className="solo-lector"> {f.nombre}</span>
            </button>
          </li>
        ))}
      </ul>

      {sinAgregar.length > 0 && (
        <>
          <p className="campo-etiqueta">Sugeridos para tu oficio</p>
          <ul className="chips">
            {sinAgregar.map((s) => (
              <li key={s.id}><button type="button" className="chip" onClick={() => agregarDelCatalogo(s)}><Icono nombre="mas" />{s.nombre}</button></li>
            ))}
          </ul>
        </>
      )}

      <div className="editor-otro">
        <label className="campo-etiqueta" htmlFor="e-otro">¿Haces algo que no está en la lista?</label>
        <div className="editor-otro-fila">
          <input id="e-otro" className="campo-entrada" value={otro} onChange={(e) => setOtro(e.target.value)} maxLength={150}
            onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); agregarPropio() } }} />
          <button type="button" className="boton boton--chico" onClick={agregarPropio}>Agregar</button>
        </div>
      </div>

      <div className="editor-acciones">
        <button className="boton boton--tinta" type="submit" disabled={guardando}>{guardando ? 'Guardando…' : 'Guardar servicios'}</button>
        <button className="boton" type="button" onClick={alCancelar} disabled={guardando}>Cancelar</button>
      </div>
    </form>
  )
}
