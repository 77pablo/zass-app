import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, Navigate } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import {
  cancelarCita, confirmarCita, diaDeChile, horaDeChile, misCitas, nuevaClaveDeIdempotencia,
  registrarAsistencia, rechazarCita, type Cita, type EstadoDeCita,
} from '../funciones/agenda/servicio'
import { miPerfil } from '../funciones/perfil/servicio'
import { useSesion } from '../sesion/contexto'
import { useTitulo } from '../marco/useTitulo'

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

function mensajeDe(e: unknown, porDefecto: string): string {
  return e instanceof ErrorApi ? e.mensaje : porDefecto
}

/**
 * Las citas de quien esta dentro, de los dos lados: las que pidio como buscador y, si ofrece un
 * servicio, las que le pidieron. De que lado esta cada una se decide comparando el perfil de la
 * cita con el propio, no con un rol: una misma persona puede ser las dos cosas con distintas
 * personas.
 */
export function MisCitas() {
  const { estado, usuario } = useSesion()
  useTitulo('Mis citas')

  const [citas, setCitas] = useState<Cita[] | null>(null)
  const [miPerfilId, setMiPerfilId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [recarga, setRecarga] = useState(0)

  const esPrestador = usuario?.roles.includes('prestador') ?? false

  useEffect(() => {
    if (estado !== 'dentro') return
    let vigente = true
    Promise.all([
      misCitas(),
      // Sin ficha todavia (o sin cuenta de prestador) no es un error: solo no hay "mi lado".
      esPrestador ? miPerfil().then((p) => p.id).catch(() => null) : Promise.resolve(null),
    ])
      .then(([lista, perfilId]) => {
        if (!vigente) return
        setCitas(lista); setMiPerfilId(perfilId); setError(null)
      })
      .catch((e: unknown) => { if (vigente) setError(mensajeDe(e, 'No pudimos cargar tus citas.')) })
    return () => { vigente = false }
  }, [estado, esPrestador, recarga])

  const recargar = useCallback(() => setRecarga((n) => n + 1), [])

  const grupos = useMemo(() => {
    const todas = citas ?? []
    return {
      proximas: todas.filter((c) => ACTIVAS.includes(c.estado)),
      porMarcar: todas.filter((c) => c.estado === 'PendienteAsistencia' && c.perfilPrestadorId === miPerfilId),
      anteriores: todas
        .filter((c) => !ACTIVAS.includes(c.estado)
          && !(c.estado === 'PendienteAsistencia' && c.perfilPrestadorId === miPerfilId))
        .reverse(),
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
          <ul className="citas-lista">
            {grupos.porMarcar.map((c) => (
              <FilaDeCita key={c.id} cita={c} miPerfilId={miPerfilId} alCambiar={recargar} />
            ))}
          </ul>
        </section>
      )}

      {grupos.proximas.length > 0 && (
        <section className="bloque" aria-labelledby="citas-proximas">
          <h2 id="citas-proximas">Próximas</h2>
          <ul className="citas-lista">
            {grupos.proximas.map((c) => (
              <FilaDeCita key={c.id} cita={c} miPerfilId={miPerfilId} alCambiar={recargar} />
            ))}
          </ul>
        </section>
      )}

      {grupos.anteriores.length > 0 && (
        <section className="bloque" aria-labelledby="citas-anteriores">
          <h2 id="citas-anteriores">Anteriores</h2>
          <ul className="citas-lista">
            {grupos.anteriores.map((c) => (
              <FilaDeCita key={c.id} cita={c} miPerfilId={miPerfilId} alCambiar={recargar} />
            ))}
          </ul>
        </section>
      )}
    </div>
  )
}

type Formulario = 'cancelar' | 'rechazar' | null

function FilaDeCita({ cita, miPerfilId, alCambiar }: {
  cita: Cita
  miPerfilId: string | null
  alCambiar: () => void
}) {
  const [formulario, setFormulario] = useState<Formulario>(null)
  const [motivo, setMotivo] = useState('')
  // Una clave por intencion: nace al abrir el formulario, no al hacer clic en enviar. Un
  // reintento tras un fallo de red reusa la misma y el servidor no cancela dos veces.
  const [clave, setClave] = useState<string | null>(null)
  const [enviando, setEnviando] = useState(false)
  const [fallo, setFallo] = useState<string | null>(null)

  const comoPrestador = miPerfilId !== null && cita.perfilPrestadorId === miPerfilId
  const activa = ACTIVAS.includes(cita.estado)

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
    setFormulario(cual); setFallo(null); setClave(nuevaClaveDeIdempotencia())
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

      <p className="cita-cuando">
        {diaDeChile(cita.inicioEn)}, {horaDeChile(cita.inicioEn)} h
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

        {activa && (
          <button type="button" className="boton boton--chico" onClick={() => abrir('cancelar')}>
            Cancelar
          </button>
        )}
      </div>

      {formulario !== null && (
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
