import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link, Navigate } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import {
  crearBloqueo, claveDeDia, DIAS, guardarDisponibilidad, horaCorta, inicioDeDiaEnChile, listarBloqueos,
  miDisponibilidad, quitarBloqueo,
  type Bloqueo, type DisponibilidadDeServicio, type Franja,
} from '../funciones/agenda/servicio'
import { useTitulo } from '../marco/useTitulo'
import { useSesion } from '../sesion/contexto'

/** Los mismos limites que valida el servidor (Zass.Aplicacion/Agenda/Validadores.cs). */
const MAXIMO_FRANJAS = 30
const DURACIONES = [15, 20, 30, 45, 60, 90, 120, 180, 240]
const MODO: Record<string, string> = {
  Automatico: 'Las horas se confirman solas al agendarse.',
  Manual: 'Confirmas tú cada hora que te piden.',
}

function mensajeDe(e: unknown, porDefecto: string): string {
  return e instanceof ErrorApi ? e.mensaje : porDefecto
}

/** Lo que el servidor rechazaria, dicho ANTES de mandarlo y junto a la franja que lo causa. */
function errorDeFranja(f: Franja): string | null {
  if (f.horaFin <= f.horaInicio) return 'La hora de término tiene que ser posterior a la de inicio.'
  const [hi, mi] = f.horaInicio.split(':').map(Number)
  const [hf, mf] = f.horaFin.split(':').map(Number)
  if ((hf * 60 + mf) - (hi * 60 + mi) < f.duracionBloqueMin) {
    return 'La franja es más corta que una hora de atención: alarga el horario o acorta la duración.'
  }
  return null
}

/** '2026-12-25' -> '2026-12-24'. Solo para restar dias de una clave ya en dia chileno. */
function diaAnterior(clave: string): string {
  const d = new Date(`${clave}T12:00:00Z`)
  d.setUTCDate(d.getUTCDate() - 1)
  return d.toISOString().slice(0, 10)
}

/** '2026-12-24' -> '24-12-2026', como se escribe la fecha en Chile. */
function fechaCorta(clave: string): string {
  const [a, m, d] = clave.split('-')
  return `${d}-${m}-${a}`
}

/**
 * Un bloqueo viaja desde el inicio del primer dia hasta el inicio del dia SIGUIENTE al ultimo
 * (asi el servidor bloquea dias completos). A la persona le importa el ultimo dia real, no ese
 * inicio que cae un dia despues.
 */
function rangoDeBloqueo(b: Bloqueo): string {
  const primero = claveDeDia(b.desdeEn)
  const ultimo = diaAnterior(claveDeDia(b.hastaEn))
  const fin = ultimo < primero ? primero : ultimo
  return primero === fin ? `El ${fechaCorta(primero)}` : `Del ${fechaCorta(primero)} al ${fechaCorta(fin)}`
}

/**
 * Mi agenda (CU-11 y CS-11). Aquí el prestador define CUÁNDO atiende, servicio por servicio, y bloquea
 * fechas. Las solicitudes de hora que le llegan se atienden en «Mis citas», que ya sabe confirmarlas,
 * rechazarlas y marcar asistencia: repetirlo aquí serían dos lugares que se desincronizan.
 */
export function MiAgenda() {
  const { estado, usuario } = useSesion()
  useTitulo('Mi agenda')
  const esPrestador = usuario?.roles.includes('prestador') ?? false

  const [servicios, setServicios] = useState<DisponibilidadDeServicio[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [recarga, setRecarga] = useState(0)

  useEffect(() => {
    if (estado !== 'dentro' || !esPrestador) return
    let vigente = true
    miDisponibilidad()
      .then((d) => { if (vigente) { setServicios(d); setError(null) } })
      .catch((e: unknown) => { if (vigente) setError(mensajeDe(e, 'No pudimos cargar tu horario.')) })
    return () => { vigente = false }
  }, [estado, esPrestador, recarga])

  if (estado === 'cargando') {
    return <div className="contenedor ficha-pagina"><p className="cargando-linea" role="status">Cargando…</p></div>
  }
  if (estado === 'fuera') return <Navigate to="/entrar" replace state={{ despues: '/mi-agenda' }} />

  if (!esPrestador) {
    return (
      <div className="contenedor ficha-pagina">
        <h1 className="pagina-titulo">Mi agenda</h1>
        <p className="bloque-texto">
          La agenda es para quien ofrece servicios. <Link to="/ofrecer-servicios">Ofrecer mis servicios</Link> con
          la misma cuenta.
        </p>
      </div>
    )
  }

  if (error) {
    return (
      <div className="contenedor ficha-pagina">
        {/* MUTACION 2: sin h1 en el estado de error */}
        <p className="aviso-linea" role="alert">
          {error}{' '}
          <button type="button" className="enlace" onClick={() => { setError(null); setRecarga((n) => n + 1) }}>
            Reintentar
          </button>
        </p>
      </div>
    )
  }
  if (servicios === null) {
    return (
      <div className="contenedor ficha-pagina">
        <h1 className="pagina-titulo">Mi agenda</h1>
        <p className="cargando-linea" role="status">Cargando tu horario…</p>
      </div>
    )
  }

  return (
    <div className="contenedor ficha-pagina agenda-prestador">
      <h1 className="pagina-titulo">Mi agenda</h1>
      <p className="bloque-texto">
        Elige cuándo atiendes cada servicio. Las horas se muestran y se guardan en hora de Chile.
        Las solicitudes que te hagan están en <Link to="/mis-citas">Mis citas</Link>.
      </p>

      {servicios.length === 0 ? (
        <p className="bloque-texto bloque-vacio">
          Todavía no ofreces ningún servicio. Agrégalos en <Link to="/mi-ficha">Mi ficha</Link> y vuelve aquí a
          definir tu horario.
        </p>
      ) : (
        servicios.map((s) => <TarjetaDeServicio key={s.prestadorServicioId} servicio={s} />)
      )}

      <BloquearFechas />
    </div>
  )
}

// ------------------------------------------------------------------ un servicio y su horario

function TarjetaDeServicio({ servicio }: { servicio: DisponibilidadDeServicio }) {
  const [franjas, setFranjas] = useState<Franja[]>(() =>
    servicio.franjas.map((f) => ({
      diaSemana: f.diaSemana, horaInicio: horaCorta(f.horaInicio), horaFin: horaCorta(f.horaFin),
      duracionBloqueMin: f.duracionBloqueMin,
    })))
  const [errores, setErrores] = useState<Record<number, string>>({})
  const [enviando, setEnviando] = useState(false)
  const [fallo, setFallo] = useState<string | null>(null)
  const [listo, setListo] = useState<number | null>(null)

  const cambiar = (i: number, parte: Partial<Franja>) => {
    setFranjas((fs) => fs.map((f, k) => (k === i ? { ...f, ...parte } : f)))
    setErrores((e) => { const { [i]: _quitado, ...resto } = e; return resto })
    setListo(null)
  }

  function agregar() {
    // El primer dia que aun no tiene franja, para no proponer siempre «lunes».
    const usados = new Set(franjas.map((f) => f.diaSemana))
    const dia = DIAS.find((d) => !usados.has(d.valor))?.valor ?? 1
    setFranjas((fs) => [...fs, { diaSemana: dia, horaInicio: '09:00', horaFin: '13:00', duracionBloqueMin: 30 }])
    setListo(null)
  }

  async function guardar() {
    if (enviando) return
    const malos: Record<number, string> = {}
    franjas.forEach((f, i) => { const m = errorDeFranja(f); if (m) malos[i] = m })
    setErrores(malos)
    if (Object.keys(malos).length > 0) return

    setEnviando(true); setFallo(null); setListo(null)
    try {
      const r = await guardarDisponibilidad(servicio.prestadorServicioId, franjas)
      setListo(r.bloquesGenerados)
    } catch (e) {
      // Lo escrito se conserva: perder el horario por un fallo de red seria castigar a quien hizo todo bien.
      setFallo(mensajeDe(e, 'No pudimos guardar tu horario. Intenta de nuevo.'))
    } finally {
      setEnviando(false)
    }
  }

  return (
    <section className="bloque agenda-servicio" aria-labelledby={`srv-${servicio.prestadorServicioId}`}
      role="region" aria-label={servicio.servicio}>
      <h2 id={`srv-${servicio.prestadorServicioId}`}>{servicio.servicio}</h2>
      <p className="bloque-texto">{MODO[servicio.modoAgenda] ?? ''}</p>

      {franjas.length === 0 && (
        <p className="bloque-texto bloque-vacio">Todavía no tiene horario: nadie puede agendar este servicio.</p>
      )}

      <ul className="franjas">
        {franjas.map((f, i) => {
          const n = i + 1
          return (
            <li key={i} className="franja">
              <div className="franja-campos">
                <label className="campo-etiqueta">Día
                  <select className="campo-entrada" aria-label={`Día, franja ${n}`} value={f.diaSemana}
                    onChange={(e) => cambiar(i, { diaSemana: Number(e.target.value) })}>
                    {DIAS.map((d) => <option key={d.valor} value={d.valor}>{d.nombre}</option>)}
                  </select>
                </label>
                <label className="campo-etiqueta">Desde
                  <input className="campo-entrada" type="time" aria-label={`Desde, franja ${n}`} value={f.horaInicio}
                    onChange={(e) => cambiar(i, { horaInicio: e.target.value })} />
                </label>
                <label className="campo-etiqueta">Hasta
                  <input className="campo-entrada" type="time" aria-label={`Hasta, franja ${n}`} value={f.horaFin}
                    onChange={(e) => cambiar(i, { horaFin: e.target.value })} />
                </label>
                <label className="campo-etiqueta">Cada hora dura
                  <select className="campo-entrada" aria-label={`Duración de cada hora, franja ${n}`} value={f.duracionBloqueMin}
                    onChange={(e) => cambiar(i, { duracionBloqueMin: Number(e.target.value) })}>
                    {DURACIONES.map((m) => <option key={m} value={m}>{m} min</option>)}
                  </select>
                </label>
                <button type="button" className="boton boton--chico"
                  aria-label={`Quitar franja ${n}` /* MUTACION 3: nombre viejo */}
                  onClick={() => { setFranjas((fs) => fs.filter((_, k) => k !== i)); setErrores({}); setListo(null) }}>
                  Quitar horario
                </button>
              </div>
              {errores[i] && <p className="aviso aviso--malo">{errores[i]}</p>}
            </li>
          )
        })}
      </ul>

      <div className="cita-acciones">
        <button type="button" className="boton boton--chico" disabled={franjas.length >= MAXIMO_FRANJAS} onClick={agregar}>
          Agregar horario
        </button>
        <button type="button" className="boton boton--amarillo boton--chico" disabled={enviando}
          onClick={() => { void guardar() }}>
          {enviando ? 'Guardando…' : 'Guardar horario'}
        </button>
      </div>

      {fallo && <p className="aviso aviso--malo" role="alert">{fallo}</p>}
      {listo !== null && (
        <p className="aviso aviso--bueno" role="status">
          Listo: quedaron {listo} horas disponibles para las próximas semanas.
        </p>
      )}
    </section>
  )
}

// ------------------------------------------------------------------ bloquear fechas

function BloquearFechas() {
  const [desde, setDesde] = useState('')
  const [hasta, setHasta] = useState('')
  const [motivo, setMotivo] = useState('')
  const [enviando, setEnviando] = useState(false)
  const [aviso, setAviso] = useState<{ tono: 'malo' | 'bueno'; texto: string } | null>(null)
  const [bloqueos, setBloqueos] = useState<Bloqueo[] | null>(null)
  const [errorBloqueos, setErrorBloqueos] = useState<string | null>(null)
  const [quitando, setQuitando] = useState<string | null>(null)
  const [recarga, setRecarga] = useState(0)

  useEffect(() => {
    let vigente = true
    listarBloqueos()
      .then((bs) => { if (vigente) { setBloqueos(bs); setErrorBloqueos(null) } })
      .catch((e: unknown) => { if (vigente) setErrorBloqueos(mensajeDe(e, 'No pudimos cargar tus bloqueos.')) })
    return () => { vigente = false }
  }, [recarga])

  const enviar = useCallback(async (e: FormEvent) => {
    e.preventDefault()
    if (enviando) return
    if (desde === '' || hasta === '') {
      setAviso({ tono: 'malo', texto: 'Elige desde qué día y hasta qué día.' })
      return
    }
    if (hasta < desde) {
      setAviso({ tono: 'malo', texto: 'El último día no puede ser anterior al primero.' })
      return
    }
    setEnviando(true); setAviso(null)
    try {
      // Dias completos en hora de Chile: del inicio del primero al inicio del dia SIGUIENTE al ultimo.
      const siguiente = new Date(`${hasta}T12:00:00Z`)
      siguiente.setUTCDate(siguiente.getUTCDate() + 1)
      await crearBloqueo(inicioDeDiaEnChile(desde), inicioDeDiaEnChile(siguiente.toISOString().slice(0, 10)), motivo)
      // J3 #3 y #4 (texto propuesto): mismo formato que la lista, y un solo día se dice como un día.
      const unDia = desde === hasta
      const cuando = `del ${desde} al ${hasta}`   // MUTACION 1: fecha cruda, sin «el» para un dia
      setAviso({ tono: 'bueno', texto: `Bloqueaste ${cuando}: nadie podrá agendar ${unDia ? 'ese día' : 'esos días'}.` })
      setDesde(''); setHasta(''); setMotivo('')
      setRecarga((n) => n + 1)
    } catch (er) {
      setAviso({ tono: 'malo', texto: mensajeDe(er, 'No pudimos bloquear esas fechas. Intenta de nuevo.') })
    } finally {
      setEnviando(false)
    }
  }, [desde, hasta, motivo, enviando])

  async function quitar(id: string) {
    if (quitando !== null) return
    setQuitando(id); setAviso(null)
    try {
      await quitarBloqueo(id)
      setAviso({ tono: 'bueno', texto: 'Bloqueo quitado: esas fechas vuelven a estar disponibles.' })
      setBloqueos((bs) => (bs ?? []).filter((b) => b.id !== id))
    } catch (e) {
      // El bloqueo sigue ahi: decir que se quito cuando no se quito seria peor que el fallo.
      setAviso({ tono: 'malo', texto: mensajeDe(e, 'No pudimos quitar ese bloqueo. Intenta de nuevo.') })
    } finally {
      setQuitando(null)
    }
  }

  return (
    <section className="bloque" role="region" aria-label="Bloquear fechas">
      <h2>Bloquear fechas</h2>
      <p className="bloque-texto">
        Para un feriado, vacaciones o una ausencia. Las horas ya agendadas esos días no se tocan.
        Si te equivocas, aquí abajo puedes quitar un bloqueo: esas fechas vuelven a estar disponibles.
      </p>
      <form className="franja-campos" onSubmit={(e) => { void enviar(e) }} noValidate>
        <label className="campo-etiqueta">Desde el día
          <input className="campo-entrada" type="date" aria-label="Desde el día" value={desde}
            onChange={(e) => { setDesde(e.target.value); setAviso(null) }} />
        </label>
        <label className="campo-etiqueta">Hasta el día
          <input className="campo-entrada" type="date" aria-label="Hasta el día" value={hasta}
            onChange={(e) => { setHasta(e.target.value); setAviso(null) }} />
        </label>
        <label className="campo-etiqueta">Motivo (opcional)
          <input className="campo-entrada" type="text" aria-label="Motivo (opcional)" maxLength={300} value={motivo}
            onChange={(e) => setMotivo(e.target.value)} />
        </label>
        <button type="submit" className="boton boton--chico" disabled={enviando}>
          {enviando ? 'Bloqueando…' : 'Bloquear'}
        </button>
      </form>
      {aviso && (
        <p className={`aviso aviso--${aviso.tono}`} role={aviso.tono === 'malo' ? 'alert' : 'status'}>{aviso.texto}</p>
      )}

      <h3>Bloqueos vigentes</h3>
      {errorBloqueos ? (
        <p className="aviso aviso--malo" role="alert">
          {errorBloqueos}{' '}
          <button type="button" className="enlace" onClick={() => setRecarga((n) => n + 1)}>Reintentar</button>
        </p>
      ) : bloqueos === null ? (
        <p className="cargando-linea" role="status">Cargando tus bloqueos…</p>
      ) : bloqueos.length === 0 ? (
        <p className="bloque-texto bloque-vacio">Ahora mismo no tienes fechas bloqueadas.</p>
      ) : (
        <ul className="franjas">
          {bloqueos.map((b, i) => (
            <li key={b.id} className="franja franja--bloqueo">
              <span>{rangoDeBloqueo(b)}</span>
              {b.motivo && <span className="franja-motivo">· {b.motivo}</span>}
              <button type="button" className="boton boton--chico" disabled={quitando !== null}
                aria-label={`Quitar bloqueo ${i + 1} (${rangoDeBloqueo(b)})`}
                onClick={() => { void quitar(b.id) }}>
                {quitando === b.id ? 'Quitando…' : 'Quitar bloqueo'}
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
