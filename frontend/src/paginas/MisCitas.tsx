import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, Navigate } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import {
  agruparPorDiaChileno, cancelarCita, confirmarCita, horaDeChile, misCitas,
  nuevaClaveDeIdempotencia, registrarAsistencia, rechazarCita, sePuedeReprogramar,
  type Cita, type EstadoDeCita,
} from '../funciones/agenda/servicio'
import { miPerfil } from '../funciones/perfil/servicio'
import { useSesion } from '../sesion/contexto'
import { useTitulo } from '../marco/useTitulo'
import { ReprogramarCita } from './ReprogramarCita'

const ETIQUETA: Record<EstadoDeCita, string> = {
  Solicitada: 'Esperando confirmación',
  Confirmada: 'Confirmada',
  Rechazada: 'Rechazada',
  Cancelada: 'Cancelada',
  Reprogramada: 'Cambiada de hora',
  PendienteAsistencia: 'Por marcar asistencia',
  Realizada: 'Realizada',
  NoAsistio: 'No asistió',
}

const ACTIVAS: EstadoDeCita[] = ['Solicitada', 'Confirmada']

/** Lo justo para saber si una cita es mia como prestador y para abrir mi propio calendario. */
type MiPerfil = { id: string; slug: string }

function mensajeDe(e: unknown, porDefecto: string): string {
  return e instanceof ErrorApi ? e.mensaje : porDefecto
}

/**
 * Las citas de quien esta dentro, de los dos lados: las que pidio como buscador y, si ofrece un
 * servicio, las que le pidieron. De que lado esta cada una se decide comparando el perfil de la
 * cita con el propio, no con un rol: una misma persona puede ser las dos cosas con distintas
 * personas.
 *
 * Esta pantalla es el ciclo de UNA cita (CU-14 y CS-12). Las reglas de atencion —que horas
 * ofrece, que dias bloquea— son otra cosa y viven en «Mi agenda»: aqui solo se enlaza.
 */
export function MisCitas() {
  const { estado, usuario } = useSesion()
  useTitulo('Mis citas')

  const [citas, setCitas] = useState<Cita[] | null>(null)
  const [mio, setMio] = useState<MiPerfil | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [recarga, setRecarga] = useState(0)

  const esPrestador = usuario?.roles.includes('prestador') ?? false

  useEffect(() => {
    if (estado !== 'dentro') return
    let vigente = true
    Promise.all([
      misCitas(),
      // Sin ficha todavia (o sin cuenta de prestador) no es un error: solo no hay "mi lado".
      esPrestador
        ? miPerfil().then((p): MiPerfil => ({ id: p.id, slug: p.slug })).catch(() => null)
        : Promise.resolve(null),
    ])
      .then(([lista, perfil]) => {
        if (!vigente) return
        setCitas(lista); setMio(perfil); setError(null)
      })
      .catch((e: unknown) => { if (vigente) setError(mensajeDe(e, 'No pudimos cargar tus citas.')) })
    return () => { vigente = false }
  }, [estado, esPrestador, recarga])

  const recargar = useCallback(() => setRecarga((n) => n + 1), [])
  const miPerfilId = mio?.id ?? null

  const grupos = useMemo(() => {
    const todas = citas ?? []
    return {
      proximas: todas.filter((c) => ACTIVAS.includes(c.estado)),
      porMarcar: todas.filter((c) => c.estado === 'PendienteAsistencia' && c.perfilPrestadorId === miPerfilId),
      anteriores: todas
        .filter((c) => !ACTIVAS.includes(c.estado)
          && !(c.estado === 'PendienteAsistencia' && c.perfilPrestadorId === miPerfilId)),
    }
  }, [citas, miPerfilId])

  if (estado === 'cargando') {
    return <div className="contenedor ficha-pagina"><p className="cargando-linea" role="status">Cargando…</p></div>
  }
  if (estado === 'fuera') return <Navigate to="/entrar" replace state={{ despues: '/mis-citas' }} />

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
  if (citas === null) {
    return <div className="contenedor ficha-pagina"><p className="cargando-linea" role="status">Cargando tus citas…</p></div>
  }

  return (
    <div className="contenedor ficha-pagina citas">
      <h1 className="pagina-titulo">Mis citas</h1>
      {esPrestador && (
        <p className="bloque-texto">
          Aquí llegan las horas que te piden. Para elegir cuándo atiendes o bloquear fechas, entra a{' '}
          <Link to="/mi-agenda">Mi agenda</Link>.
        </p>
      )}

      {citas.length === 0 && (
        <p className="bloque-texto bloque-vacio">
          Todavía no tienes citas. <Link to="/buscar">Busca a alguien</Link> y agenda una hora desde su ficha.
        </p>
      )}

      {grupos.porMarcar.length > 0 && (
        <section className="bloque" aria-labelledby="citas-por-marcar">
          <h2 id="citas-por-marcar">Por marcar asistencia</h2>
          <p className="bloque-texto">
            Ya pasó la hora. Si no la marcas en 72 horas se da por realizada.
          </p>
          <PorDia citas={grupos.porMarcar} orden="desc" mio={mio} alCambiar={recargar} />
        </section>
      )}

      {grupos.proximas.length > 0 && (
        <section className="bloque" aria-labelledby="citas-proximas">
          <h2 id="citas-proximas">Próximas</h2>
          <PorDia citas={grupos.proximas} orden="asc" mio={mio} alCambiar={recargar} />
        </section>
      )}

      {grupos.anteriores.length > 0 && (
        <section className="bloque" aria-labelledby="citas-anteriores">
          <h2 id="citas-anteriores">Anteriores</h2>
          <PorDia citas={grupos.anteriores} orden="desc" mio={mio} alCambiar={recargar} />
        </section>
      )}
    </div>
  )
}

/**
 * Un dia CHILENO por grupo, con la fecha de titulo y las horas debajo. Agrupar por el dia del
 * navegador pondria la cita de las 22:00 de un jueves bajo el viernes para quien mira desde
 * Europa: la hora que importa es la de pared de Chile, que es la que el prestador publico.
 */
function PorDia({ citas, orden, mio, alCambiar }: {
  citas: Cita[]
  orden: 'asc' | 'desc'
  mio: MiPerfil | null
  alCambiar: () => void
}) {
  return (
    <ul className="citas-dias">
      {agruparPorDiaChileno(citas, orden).map((d) => (
        <li key={d.dia}>
          <h3 className="citas-dia">{d.titulo}</h3>
          <ul className="citas-lista">
            {d.items.map((c) => (
              <FilaDeCita key={c.id} cita={c} mio={mio} alCambiar={alCambiar} />
            ))}
          </ul>
        </li>
      ))}
    </ul>
  )
}

type Formulario = 'cancelar' | 'rechazar' | 'reprogramar' | null

function FilaDeCita({ cita, mio, alCambiar }: {
  cita: Cita
  mio: MiPerfil | null
  alCambiar: () => void
}) {
  const [formulario, setFormulario] = useState<Formulario>(null)
  const [motivo, setMotivo] = useState('')
  // Una clave por intencion: nace al abrir el formulario, no al hacer clic en enviar. Un
  // reintento tras un fallo de red reusa la misma y el servidor no cancela dos veces.
  const [clave, setClave] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [fallo, setFallo] = useState<string | null>(null)

  const comoPrestador = mio !== null && cita.perfilPrestadorId === mio.id
  const activa = ACTIVAS.includes(cita.estado)
  const puedeReprogramar = sePuedeReprogramar(cita, comoPrestador)
  // 5B de CU-14: al buscador se le cierra la puerta a las 2 horas, pero se le dice por que y
  // que le queda. Al prestador nunca se le cierra, asi que este aviso no es para el.
  const tardeParaMover = cita.estado === 'Confirmada' && !comoPrestador && !puedeReprogramar

  async function correr(accion: () => Promise<unknown>) {
    if (enviando) return
    setEnviando(true); setFallo(null)
    try {
      await accion()
      setFormulario(null); setMotivo(''); setClave(null)
      alCambiar()
    } catch (e) {
      setFallo(mensajeDe(e, 'No pudimos completar la acción. Intenta de nuevo.'))
    } finally {
      setEnviando(false)
    }
  }

  function abrir(cual: Exclude<Formulario, null>) {
    setFormulario(cual); setFallo(null)
    // La de reprogramar nace al elegir la hora nueva, dentro del panel: hasta entonces no hay
    // intencion que proteger.
    setClave(cual === 'cancelar' ? nuevaClaveDeIdempotencia() : null)
  }

  return (
    <li className="cita">
      <div className="cita-cabecera">
        <div>
          <strong>{cita.servicio}</strong>
          <span className="cita-con">
            {comoPrestador ? 'Te pidieron esta hora' : `con ${cita.prestador}`}
          </span>
        </div>
        <span className={activa ? 'dato' : 'dato dato--apagado'}>{ETIQUETA[cita.estado]}</span>
      </div>

      {/* El dia ya lo pone el titulo del grupo; aqui solo la hora, en hora de Chile. */}
      <p className="cita-cuando">
        {horaDeChile(cita.inicioEn)} h
        <span className="cita-codigo">{cita.codigo}</span>
      </p>

      {cita.notaBuscador && <p className="cita-nota">“{cita.notaBuscador}”</p>}

      <div className="cita-acciones">
        {comoPrestador && cita.estado === 'Solicitada' && (
          <>
            <button type="button" className="boton boton--chico boton--amarillo" disabled={enviando}
              onClick={() => { void correr(() => confirmarCita(cita.id)) }}>
              Confirmar
            </button>
            <button type="button" className="boton boton--chico" onClick={() => abrir('rechazar')}>
              Rechazar
            </button>
          </>
        )}

        {comoPrestador && cita.estado === 'PendienteAsistencia' && (
          <>
            <button type="button" className="boton boton--chico boton--amarillo" disabled={enviando}
              onClick={() => { void correr(() => registrarAsistencia(cita.id, true)) }}>
              Asistió
            </button>
            <button type="button" className="boton boton--chico" disabled={enviando}
              onClick={() => { void correr(() => registrarAsistencia(cita.id, false)) }}>
              No asistió
            </button>
          </>
        )}

        {puedeReprogramar && formulario !== 'reprogramar' && (
          <button type="button" className="boton boton--chico" onClick={() => abrir('reprogramar')}>
            Reprogramar
          </button>
        )}

        {activa && (
          <button type="button" className="boton boton--chico" onClick={() => abrir('cancelar')}>
            Cancelar
          </button>
        )}
      </div>

      {tardeParaMover && (
        <p className="cita-nota">
          Faltan menos de 2 horas: ya no puedes cambiarla de hora. Puedes cancelarla explicando el motivo.
        </p>
      )}

      {formulario === 'reprogramar' && (
        <ReprogramarCita
          cita={cita}
          alTerminar={() => { setFormulario(null); alCambiar() }}
          alCerrar={() => setFormulario(null)}
        />
      )}

      {(formulario === 'cancelar' || formulario === 'rechazar') && (
        <div className="cita-formulario">
          <label className="campo-nota">
            <span>
              {formulario === 'cancelar'
                ? 'Motivo (obligatorio si faltan menos de 12 horas)'
                : 'Motivo (opcional)'}
            </span>
            <textarea value={motivo} rows={2} maxLength={300} onChange={(e) => setMotivo(e.target.value)} />
          </label>
          <div className="cita-acciones">
            <button type="button" className="boton boton--chico boton--amarillo" disabled={enviando}
              onClick={() => {
                void correr(() => formulario === 'cancelar'
                  ? cancelarCita(cita.id, clave ?? nuevaClaveDeIdempotencia(), motivo.trim() || null)
                  : rechazarCita(cita.id, motivo.trim() || null))
              }}>
              {formulario === 'cancelar' ? 'Cancelar la hora' : 'Rechazar la hora'}
            </button>
            <button type="button" className="boton boton--chico" onClick={() => setFormulario(null)}>
              Volver
            </button>
          </div>
        </div>
      )}

      {fallo !== null && <p className="aviso-linea" role="alert">{fallo}</p>}
    </li>
  )
}
