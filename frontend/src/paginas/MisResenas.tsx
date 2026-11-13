import { useCallback, useEffect, useState } from 'react'
import { Link, Navigate } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import { Estrellas } from '../componentes/resenas/Estrellas'
import { FormularioDeResena } from '../componentes/resenas/FormularioDeResena'
import { diaDeChile, horaDeChile } from '../funciones/agenda/servicio'
import { type Borrador } from '../funciones/resenas/borrador'
import { plazoDeEdicion } from '../funciones/resenas/plazo'
import {
  ASPECTOS, editar, misResenas,
  type AvisoDeDatosPersonales, type MiResena,
} from '../funciones/resenas/servicio'
import * as T from '../funciones/resenas/textos'
import { useSesion } from '../sesion/contexto'
import { useTitulo } from '../marco/useTitulo'

const RUTA = '/mis-resenas'

const ETIQUETA_DE_ASPECTO = new Map(ASPECTOS.map((a) => [a.valor as string, a.etiqueta as string]))

function mensajeDe(e: unknown, porDefecto: string): string {
  return e instanceof ErrorApi ? e.mensaje : porDefecto
}

/** El instante exacto hasta el que se puede editar, en hora de Chile. */
function cuando(iso: string): string {
  return `${diaDeChile(iso)} a las ${horaDeChile(iso)} h`
}

/**
 * CS-16 · Mis reseñas (API-65). La razon de ser de esta pantalla es que la ventana de edicion de
 * 72 h existia solo en el servidor y en la pagina donde se acababa de publicar: quien recargaba
 * no tenia NINGUNA forma de volver a su propia reseña.
 *
 * El orden lo garantiza el servidor (de la mas nueva a la mas vieja) y aqui no se vuelve a
 * ordenar: dos criterios de orden son dos criterios que pueden discrepar.
 */
export function MisResenas() {
  const { estado } = useSesion()
  useTitulo(T.TITULO)

  const [resenas, setResenas] = useState<MiResena[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [intento, setIntento] = useState(0)

  useEffect(() => {
    if (estado !== 'dentro') return
    let vigente = true
    misResenas()
      .then((lista) => { if (vigente) { setResenas(lista); setError(null) } })
      .catch((e: unknown) => { if (vigente) setError(mensajeDe(e, T.ERROR_CARGA)) })
    return () => { vigente = false }
  }, [estado, intento])

  const recargar = useCallback(() => {
    setResenas(null); setError(null); setIntento((n) => n + 1)
  }, [])

  /**
   * Tras editar, la fila se reemplaza en el sitio. El servidor devuelve la reseña del AUTOR, que
   * no trae el nombre ni el slug del prestador: esos se conservan de la fila que ya teniamos, en
   * vez de recargar la lista entera y perder el lugar donde estaba leyendo.
   */
  const reemplazar = useCallback((cambiada: MiResena) => {
    setResenas((previas) => previas?.map((r) => (r.id === cambiada.id ? cambiada : r)) ?? previas)
  }, [])

  if (estado === 'cargando') {
    return <div className="contenedor ficha-pagina"><p className="cargando-linea" role="status">Cargando…</p></div>
  }
  if (estado === 'fuera') return <Navigate to="/entrar" replace state={{ despues: RUTA }} />

  if (error !== null) {
    return (
      <div className="contenedor ficha-pagina">
        <p className="aviso-linea" role="alert">
          {error}{' '}
          <button type="button" className="enlace" onClick={recargar}>{T.REINTENTAR}</button>
        </p>
      </div>
    )
  }
  if (resenas === null) {
    return <div className="contenedor ficha-pagina"><p className="cargando-linea" role="status">{T.CARGANDO}</p></div>
  }

  return (
    <div className="contenedor ficha-pagina resenas-pagina">
      <h1 className="pagina-titulo">{T.TITULO}</h1>

      {resenas.length === 0 ? (
        <p className="bloque-texto bloque-vacio">
          {T.VACIO}. {T.VACIO_DETALLE} <Link to="/buscar">{T.VACIO_ENLACE}</Link>.
        </p>
      ) : (
        <>
          <p className="bloque-texto">{T.INTRO}</p>
          <ul className="resenas-lista">
            {resenas.map((r) => (
              <li key={r.id}>
                <MiTarjeta resena={r} alCambiar={reemplazar} />
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  )
}

type Envio =
  | { tipo: 'mirando' }
  | { tipo: 'editando' }
  | { tipo: 'enviando' }
  | { tipo: 'avisa'; aviso: AvisoDeDatosPersonales }
  | { tipo: 'fallo'; mensaje: string }

/** Una reseña mia: lo que escribi, a quien, en que estado quedo y si todavia la puedo cambiar. */
function MiTarjeta({ resena, alCambiar }: { resena: MiResena; alCambiar: (r: MiResena) => void }) {
  const [envio, setEnvio] = useState<Envio>({ tipo: 'mirando' })
  const [borrador, setBorrador] = useState<Borrador>({
    calificacion: resena.calificacion, comentario: resena.comentario, aspectos: [...resena.aspectos],
  })

  // El plazo se mide con la MISMA funcion que ya usa la pagina de publicar; null es "se acabo".
  const queda = plazoDeEdicion(resena.editableHasta)
  // Estado antes que plazo, igual que el servidor: una reseña retirada u oculta no se edita
  // aunque este a tiempo. Ofrecer el boton seria prometer algo que el servidor va a rechazar.
  const sePuedeEditar = resena.estado === 'Publicada' && queda !== null

  async function guardar(enviarIgual: boolean) {
    if (envio.tipo === 'enviando' || borrador.calificacion === null) return
    setEnvio({ tipo: 'enviando' })
    try {
      const r = await editar(
        resena.id, borrador.calificacion, borrador.comentario.trim(), borrador.aspectos, enviarIgual)
      if (r.tipo === 'datos-personales') {
        setEnvio({ tipo: 'avisa', aviso: r.aviso })
      } else {
        // Campo a campo: la reseña del autor NO trae el nombre ni el slug del prestador, y un
        // volcado entero los borraria (y colaria `perfilPrestadorId`, que aqui no pinta nada).
        alCambiar({
          ...resena,
          calificacion: r.resena.calificacion,
          comentario: r.resena.comentario,
          aspectos: r.resena.aspectos,
          estado: r.resena.estado,
          editada: r.resena.editada,
          publicadaEn: r.resena.publicadaEn,
          editableHasta: r.resena.editableHasta,
        })
        setEnvio({ tipo: 'mirando' })
      }
    } catch (e) {
      setEnvio({ tipo: 'fallo', mensaje: mensajeDe(e, 'No pudimos guardar tu cambio. Intenta de nuevo.') })
    }
  }

  const editando = envio.tipo !== 'mirando'

  return (
    <article className="resena">
      <header className="resena-cabecera">
        <h2 className="resena-prestador">
          <Link to={`/prestadores/${resena.perfilSlug}`}>{resena.perfilNombre}</Link>
        </h2>
        <span className="resena-fecha">
          {diaDeChile(resena.publicadaEn)}
          {resena.editada && <span className="resena-editada"> · {T.EDITADA}</span>}
        </span>
      </header>

      {/* Lo que paso con ella, si dejo de estar publicada. Una reseña retirada sigue siendo suya. */}
      {resena.estado === 'Retirada' && <p className="resena-rotulo">{T.ESTADO_RETIRADA}</p>}
      {resena.estado === 'OcultaEnModeracion' && <p className="resena-rotulo">{T.ESTADO_OCULTA}</p>}

      {resena.tipo === 'Inasistencia' && <p className="resena-rotulo">{T.POR_INASISTENCIA}</p>}
      {resena.origen === 'Directo' && <p className="resena-rotulo">{T.ORIGEN_DIRECTO}</p>}

      {editando ? (
        <section>
          <h3>{T.EDITAR_TITULO}</h3>
          <FormularioDeResena
            borrador={borrador}
            alCambiar={setBorrador}
            alCorregir={() => setEnvio({ tipo: 'editando' })}
            alEnviar={(igual) => { void guardar(igual) }}
            enviando={envio.tipo === 'enviando'}
            aviso={envio.tipo === 'avisa' ? envio.aviso : null}
            fallo={envio.tipo === 'fallo' ? envio.mensaje : null}
            etiquetaEnviar={T.GUARDAR}
            alCancelar={() => {
              setBorrador({
                calificacion: resena.calificacion,
                comentario: resena.comentario,
                aspectos: [...resena.aspectos],
              })
              setEnvio({ tipo: 'mirando' })
            }}
          />
        </section>
      ) : (
        <>
          <Estrellas valor={resena.calificacion} />
          <p className="texto-largo">{resena.comentario}</p>

          {resena.aspectos.length > 0 && (
            <ul className="resena-etiquetas">
              {resena.aspectos.map((a) => (
                <li key={a} className="resena-etiqueta">{ETIQUETA_DE_ASPECTO.get(a) ?? a}</li>
              ))}
            </ul>
          )}

          {sePuedeEditar ? (
            <div className="resena-acciones">
              <p className="letra-chica">{T.plazoVivo(cuando(resena.editableHasta))}</p>
              <button type="button" className="boton boton--chico" onClick={() => setEnvio({ tipo: 'editando' })}>
                {T.EDITAR}
              </button>
            </div>
          ) : (
            // Pasado el plazo (o fuera de «Publicada») se DICE, en vez de dejar un boton que
            // el servidor va a rechazar despues de hacer escribir a alguien.
            resena.estado === 'Publicada' && <p className="letra-chica">{T.PLAZO_VENCIDO}</p>
          )}
        </>
      )}
    </article>
  )
}
