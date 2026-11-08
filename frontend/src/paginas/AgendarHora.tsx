import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import type { FichaPublica } from '../funciones/busqueda/servicio'
import {
  agendar, calendarioDe, diaDeChile, horaDeChile, nuevaClaveDeIdempotencia, porDia,
  type Bloque, type Calendario, type Cita,
} from '../funciones/agenda/servicio'
import { useSesion } from '../sesion/contexto'
import { duracion } from '../marco/texto'

type Servicio = FichaPublica['servicios'][number]

const DIAS_VISIBLES = 7
const LARGO_NOTA = 500

type Carga =
  | { tipo: 'cargando' }
  | { tipo: 'ok'; calendario: Calendario }
  | { tipo: 'error'; mensaje: string }

/**
 * CU-12 en la ficha: elegir servicio, elegir hora, confirmar. Hasta el calendario es publico;
 * la cuenta se pide recien al confirmar, y quien no la tiene vuelve aqui con la hora elegida
 * (flujo 4G) porque la ruta de regreso lleva el servicio y el bloque en la direccion.
 */
export function AgendarHora({ slug, nombre, servicios, elegidoInicial }: {
  slug: string
  nombre: string
  servicios: Servicio[]
  /** Del flujo 4G: lo que la persona habia elegido antes de entrar. */
  elegidoInicial?: { servicio: string | null; bloque: string | null }
}) {
  const { estado: sesion, usuario } = useSesion()
  const [servicioId, setServicioId] = useState<string | null>(
    elegidoInicial?.servicio && servicios.some((s) => s.id === elegidoInicial.servicio)
      ? elegidoInicial.servicio
      : servicios.length === 1 ? servicios[0].id : null,
  )
  const [carga, setCarga] = useState<Carga>({ tipo: 'cargando' })
  const [intento, setIntento] = useState(0)
  const [elegido, setElegido] = useState<string | null>(elegidoInicial?.bloque ?? null)
  const [diasVistos, setDiasVistos] = useState(DIAS_VISIBLES)

  // La clave nace al ELEGIR la hora, no al hacer clic en confirmar: un doble clic reusa la
  // misma y el servidor devuelve la misma cita en vez de crear una segunda (CC-04).
  const [clave, setClave] = useState<string | null>(
    elegidoInicial?.bloque ? nuevaClaveDeIdempotencia() : null,
  )
  const [nota, setNota] = useState('')
  const [enviando, setEnviando] = useState(false)
  const [fallo, setFallo] = useState<string | null>(null)
  const [cita, setCita] = useState<Cita | null>(null)

  const servicio = servicios.find((s) => s.id === servicioId) ?? null

  useEffect(() => {
    if (servicioId === null) return
    let vigente = true
    calendarioDe(slug, servicioId)
      .then((calendario) => { if (vigente) setCarga({ tipo: 'ok', calendario }) })
      .catch((e: unknown) => {
        if (vigente) setCarga({
          tipo: 'error',
          mensaje: e instanceof ErrorApi ? e.mensaje : 'No pudimos cargar las horas.',
        })
      })
    return () => { vigente = false }
  }, [slug, servicioId, intento])

  const libres = useMemo(
    () => (carga.tipo === 'ok' ? carga.calendario.bloques.filter((b) => b.estado === 'libre') : []),
    [carga],
  )
  const dias = useMemo(() => porDia(libres), [libres])
  const bloqueElegido: Bloque | null = libres.find((b) => b.id === elegido) ?? null

  function elegirServicio(id: string) {
    setServicioId(id)
    setCarga({ tipo: 'cargando' })
    setElegido(null); setClave(null); setFallo(null); setCita(null); setDiasVistos(DIAS_VISIBLES)
  }

  function elegirBloque(id: string) {
    setElegido(id)
    setClave(nuevaClaveDeIdempotencia())   // otra hora es otra intencion
    setFallo(null)
  }

  async function confirmar() {
    if (bloqueElegido === null || clave === null || enviando) return
    setEnviando(true); setFallo(null)
    try {
      setCita(await agendar(bloqueElegido.id, clave, nota))
    } catch (e) {
      setFallo(e instanceof ErrorApi ? e.mensaje : 'No pudimos agendar. Intenta de nuevo.')
      // Flujo 4A/4D: la hora ya no esta. Se recarga el calendario y se suelta la eleccion.
      if (e instanceof ErrorApi && (e.estado === 409 || e.estado === 404)) {
        setElegido(null); setClave(null)
        setCarga({ tipo: 'cargando' }); setIntento((n) => n + 1)
      }
    } finally {
      setEnviando(false)
    }
  }

  // Se vuelve a esta misma ficha con lo elegido, para retomar en el paso de confirmar (4G).
  const regreso = servicioId && bloqueElegido
    ? `/prestadores/${slug}?servicio=${servicioId}&bloque=${bloqueElegido.id}`
    : `/prestadores/${slug}`

  if (cita !== null) return <Listo cita={cita} />

  const primerNombre = nombre.split(' ')[0]

  return (
    <section className="bloque agenda" aria-labelledby="agenda-titulo">
      <h2 id="agenda-titulo">Agendar una hora con {primerNombre}</h2>

      {servicios.length === 0 && (
        <p className="bloque-texto bloque-vacio">Todavía no publicó sus servicios.</p>
      )}

      {servicios.length > 1 && (
        <div className="agenda-servicios" role="group" aria-label="Servicio">
          {servicios.map((s) => (
            <button
              key={s.id} type="button" aria-pressed={s.id === servicioId}
              className={s.id === servicioId ? 'ficha-opcion ficha-opcion--activa' : 'ficha-opcion'}
              onClick={() => elegirServicio(s.id)}
            >
              <strong>{s.nombre}</strong>
              <span>{duracion(s.duracionMinutos)}</span>
            </button>
          ))}
        </div>
      )}

      {servicio === null && servicios.length > 1 && (
        <p className="bloque-texto bloque-vacio">Elige un servicio para ver las horas.</p>
      )}

      {servicio !== null && servicio.modoAgenda === 'Manual' && (
        <p className="agenda-aviso" role="note">
          Con este servicio {primerNombre} confirma cada hora a mano. Pides la hora ahora y
          tiene 24 horas para responder; si no lo hace, la hora se libera sola.
        </p>
      )}

      {servicio !== null && carga.tipo === 'cargando' && (
        <p className="cargando-linea" role="status">Buscando horas libres…</p>
      )}

      {servicio !== null && carga.tipo === 'error' && (
        <p className="aviso-linea" role="alert">
          {carga.mensaje}{' '}
          <button type="button" className="enlace"
            onClick={() => { setCarga({ tipo: 'cargando' }); setIntento((n) => n + 1) }}>
            Reintentar
          </button>
        </p>
      )}

      {servicio !== null && carga.tipo === 'ok' && dias.length === 0 && (
        <p className="bloque-texto bloque-vacio">
          {primerNombre} no tiene horas libres por ahora. Puedes escribirle directo desde el costado.
        </p>
      )}

      {servicio !== null && carga.tipo === 'ok' && dias.length > 0 && (
        <>
          <ul className="agenda-dias">
            {dias.slice(0, diasVistos).map((d) => (
              <li key={d.dia}>
                <h3 className="agenda-dia">{d.titulo}</h3>
                <div className="agenda-horas">
                  {d.bloques.map((b) => (
                    <button
                      key={b.id} type="button" aria-pressed={b.id === elegido}
                      className={b.id === elegido ? 'hora hora--activa' : 'hora'}
                      onClick={() => elegirBloque(b.id)}
                    >
                      {horaDeChile(b.inicioEn)}
                    </button>
                  ))}
                </div>
              </li>
            ))}
          </ul>
          {dias.length > diasVistos && (
            <button type="button" className="enlace" onClick={() => setDiasVistos((n) => n + DIAS_VISIBLES)}>
              Ver más días
            </button>
          )}
        </>
      )}

      {bloqueElegido !== null && servicio !== null && (
        <div className="agenda-resumen">
          <h3>Tu hora</h3>
          <p>
            <strong>{servicio.nombre}</strong><br />
            {diaDeChile(bloqueElegido.inicioEn)}, {horaDeChile(bloqueElegido.inicioEn)} h (hora de Chile)
          </p>

          {sesion === 'dentro' && usuario?.correoVerificado ? (
            <>
              <label className="campo-nota">
                <span>Algo que {primerNombre} deba saber (opcional)</span>
                <textarea
                  value={nota} maxLength={LARGO_NOTA} rows={3}
                  onChange={(e) => setNota(e.target.value)}
                />
                <small>{nota.length}/{LARGO_NOTA}</small>
              </label>
              <button type="button" className="boton boton--amarillo boton--lleno"
                disabled={enviando} onClick={() => { void confirmar() }}>
                {enviando ? 'Agendando…' : servicio.modoAgenda === 'Manual' ? 'Pedir esta hora' : 'Confirmar esta hora'}
              </button>
            </>
          ) : sesion === 'dentro' ? (
            <p className="agenda-aviso" role="note">Verifica tu correo para agendar. Te lo enviamos al registrarte.</p>
          ) : (
            <p className="agenda-entrar">
              Para reservarla necesitas una cuenta. Es gratis para quien busca.{' '}
              <Link to="/entrar" state={{ despues: regreso }}>Entrar</Link> o{' '}
              <Link to="/crear-cuenta" state={{ despues: regreso }}>crear mi cuenta</Link>; volverás aquí con esta hora elegida.
            </p>
          )}

        </div>
      )}

      {/* Fuera del panel de la hora: tras un 409 la eleccion se suelta y el panel desaparece, y el
          aviso de POR QUE tiene que quedar a la vista. */}
      {fallo !== null && <p className="aviso-linea" role="alert">{fallo}</p>}
    </section>
  )
}

/** CU-12 paso 15: fecha, hora, servicio y codigo. */
function Listo({ cita }: { cita: Cita }) {
  const manual = cita.estado === 'Solicitada'
  return (
    <section className="bloque agenda agenda--lista" role="status">
      <h2>{manual ? 'Pedimos tu hora' : 'Tu hora está confirmada'}</h2>
      <p className="agenda-codigo">{cita.codigo}</p>
      <p>
        <strong>{cita.servicio}</strong> con {cita.prestador}<br />
        {diaDeChile(cita.inicioEn)}, {horaDeChile(cita.inicioEn)} h (hora de Chile)
      </p>
      {manual && (
        <p className="agenda-aviso">
          Queda pendiente de que {cita.prestador.split(' ')[0]} la confirme. Si no responde en 24 horas, la hora se libera.
        </p>
      )}
      <Link to="/mis-citas" className="boton boton--chico">Ver mis citas</Link>
    </section>
  )
}
