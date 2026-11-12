import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import {
  calendarioDe, diaDeChile, horaDeChile, nuevaClaveDeIdempotencia, porDia, reprogramarCita,
  type Bloque, type Calendario, type Cita,
} from '../funciones/agenda/servicio'
import { slugDelPrestador } from '../funciones/agenda/slug-del-prestador'

const DIAS_VISIBLES = 7
const LARGO_MOTIVO = 300

type Carga =
  | { tipo: 'cargando' }
  | { tipo: 'ok'; calendario: Calendario }
  /** El prestador no tiene ficha publica alcanzable: su calendario tampoco existiria. */
  | { tipo: 'sin-ficha' }
  | { tipo: 'error'; mensaje: string }

/**
 * CU-14, flujo principal de reprogramar: ver las horas libres del MISMO servicio, elegir una,
 * escribir un motivo opcional y confirmar. Reprogramar es cancelar y crear (RN-S4-05): la cita
 * nueva queda ligada a la primera de la cadena, asi que cambiar de hora **no** hace perder la
 * posibilidad de resenar. Eso se le dice a la persona, porque es la duda que frena el cambio.
 *
 * El panel no promete ningun aviso a la otra parte: hoy el servidor no manda ninguno.
 */
export function ReprogramarCita({ cita, miPerfil, alTerminar, alCerrar }: {
  cita: Cita
  /** Mi perfil de prestador, si tengo uno. Sirve para resolver el slug sin salir a buscarlo. */
  miPerfil: { id: string; slug: string } | null
  alTerminar: () => void
  alCerrar: () => void
}) {
  const [carga, setCarga] = useState<Carga>({ tipo: 'cargando' })
  const [intento, setIntento] = useState(0)
  const [elegido, setElegido] = useState<string | null>(null)
  const [diasVistos, setDiasVistos] = useState(DIAS_VISIBLES)
  // Una clave por INTENCION: nace al elegir la hora. Un reintento tras un fallo de red lleva la
  // misma y el servidor devuelve la cita que ya nacio, no una segunda (CC-04).
  const [clave, setClave] = useState<string | null>(null)
  const [motivo, setMotivo] = useState('')
  const [enviando, setEnviando] = useState(false)
  const [fallo, setFallo] = useState<string | null>(null)
  const [nueva, setNueva] = useState<Cita | null>(null)

  useEffect(() => {
    let vigente = true
    slugDelPrestador(cita, miPerfil)
      .then(async (slug) => {
        if (slug === null) {
          if (vigente) setCarga({ tipo: 'sin-ficha' })
          return
        }
        const calendario = await calendarioDe(slug, cita.prestadorServicioId)
        if (vigente) setCarga({ tipo: 'ok', calendario })
      })
      .catch((e: unknown) => {
        if (vigente) setCarga({
          tipo: 'error',
          mensaje: e instanceof ErrorApi ? e.mensaje : 'No pudimos cargar las horas libres.',
        })
      })
    return () => { vigente = false }
    // `cita` y `miPerfil` son estables mientras el panel esta abierto: la lista se recarga entera.
  }, [cita, miPerfil, intento])

  const libres = useMemo(
    () => (carga.tipo === 'ok' ? carga.calendario.bloques.filter((b) => b.estado === 'libre') : []),
    [carga],
  )
  const dias = useMemo(() => porDia(libres), [libres])
  const bloque: Bloque | null = libres.find((b) => b.id === elegido) ?? null

  function recargarElCalendario() {
    setElegido(null); setClave(null)
    setCarga({ tipo: 'cargando' }); setIntento((n) => n + 1)
  }

  async function confirmar() {
    if (bloque === null || clave === null || enviando) return
    setEnviando(true); setFallo(null)
    try {
      setNueva(await reprogramarCita(cita.id, bloque.id, clave, motivo.trim() || null))
    } catch (e) {
      setFallo(e instanceof ErrorApi ? e.mensaje : 'No pudimos cambiar la hora. Intenta de nuevo.')
      // Flujo 5A: la hora destino se ocupo mientras elegia. Nada cambio, la cita sigue en pie;
      // se suelta la eleccion y se recarga el calendario. El aviso vive FUERA del panel que
      // desaparece, que es el defecto que ya se cazo una vez en AgendarHora.
      if (e instanceof ErrorApi && (e.estado === 409 || e.estado === 404)) recargarElCalendario()
    } finally {
      setEnviando(false)
    }
  }

  if (nueva !== null) {
    return (
      <div className="cita-formulario" role="status">
        <h3 className="cita-subtitulo">Tu hora quedó cambiada</h3>
        <p className="agenda-codigo">{nueva.codigo}</p>
        <p>{diaDeChile(nueva.inicioEn)}, {horaDeChile(nueva.inicioEn)} h (hora de Chile)</p>
        <p className="cita-nota">
          Sigue contando como la misma cita: cambiar la hora no te quita la posibilidad de reseñar.
        </p>
        <button type="button" className="boton boton--chico boton--amarillo" onClick={alTerminar}>
          Listo
        </button>
      </div>
    )
  }

  return (
    <div className="cita-formulario">
      <h3 className="cita-subtitulo">Cambiar la hora</h3>
      <p className="cita-nota">
        Hoy: {diaDeChile(cita.inicioEn)}, {horaDeChile(cita.inicioEn)} h. Elige otra del mismo servicio.
      </p>

      {carga.tipo === 'cargando' && (
        <p className="cargando-linea" role="status">Buscando horas libres…</p>
      )}

      {carga.tipo === 'sin-ficha' && (
        <p className="bloque-texto bloque-vacio">
          No pudimos abrir el calendario de {cita.prestador}. Puedes{' '}
          <Link to="/buscar">buscarlo</Link> y agendar desde su ficha, o cancelar esta hora.
        </p>
      )}

      {carga.tipo === 'error' && (
        <p className="aviso-linea" role="alert">
          {carga.mensaje}{' '}
          <button type="button" className="enlace"
            onClick={() => { setCarga({ tipo: 'cargando' }); setIntento((n) => n + 1) }}>
            Reintentar
          </button>
        </p>
      )}

      {carga.tipo === 'ok' && dias.length === 0 && (
        <p className="bloque-texto bloque-vacio">
          {cita.prestador} no tiene otras horas libres por ahora.
        </p>
      )}

      {carga.tipo === 'ok' && dias.length > 0 && (
        <>
          <ul className="agenda-dias">
            {dias.slice(0, diasVistos).map((d) => (
              <li key={d.dia}>
                <h4 className="agenda-dia">{d.titulo}</h4>
                <div className="agenda-horas">
                  {d.bloques.map((b) => (
                    <button
                      key={b.id} type="button" aria-pressed={b.id === elegido}
                      className={b.id === elegido ? 'hora hora--activa' : 'hora'}
                      onClick={() => {
                        setElegido(b.id)
                        setClave(nuevaClaveDeIdempotencia())   // otra hora es otra intencion
                        setFallo(null)
                      }}
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

      {bloque !== null && (
        <div className="agenda-resumen">
          <h4>El cambio</h4>
          <p>
            De {diaDeChile(cita.inicioEn)}, {horaDeChile(cita.inicioEn)} h<br />
            a <strong>{diaDeChile(bloque.inicioEn)}, {horaDeChile(bloque.inicioEn)} h</strong> (hora de Chile)
          </p>
          <label className="campo-nota">
            <span>Motivo (opcional)</span>
            <textarea value={motivo} rows={2} maxLength={LARGO_MOTIVO}
              onChange={(e) => setMotivo(e.target.value)} />
          </label>
          <button type="button" className="boton boton--chico boton--amarillo" disabled={enviando}
            onClick={() => { void confirmar() }}>
            {enviando ? 'Cambiando…' : 'Confirmar el cambio'}
          </button>
        </div>
      )}

      <div className="cita-acciones">
        <button type="button" className="boton boton--chico" onClick={alCerrar}>Volver</button>
      </div>

      {/* Fuera del panel de la hora elegida: tras un 409 la eleccion se suelta y el panel
          desaparece, y el aviso de POR QUE tiene que quedar a la vista. */}
      {fallo !== null && <p className="aviso-linea" role="alert">{fallo}</p>}
    </div>
  )
}
