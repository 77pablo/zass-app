import { useCallback, useEffect, useState } from 'react'
import { Link, Navigate, useParams } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import { FormularioDeResena } from '../componentes/resenas/FormularioDeResena'
import { BORRADOR_VACIO, type Borrador } from '../funciones/resenas/borrador'
import { Estrellas } from '../componentes/resenas/Estrellas'
import { fichaPublica, type FichaPublica } from '../funciones/busqueda/servicio'
import { diaDeChile } from '../funciones/agenda/servicio'
import { plazoDeEdicion } from '../funciones/resenas/plazo'
import {
  editar, elegibilidad, nuevaClaveDeIdempotencia, publicar, registrarContacto,
  type AvisoDeDatosPersonales, type Elegibilidad, type Resena,
} from '../funciones/resenas/servicio'
import { useSesion } from '../sesion/contexto'
import { useTitulo } from '../marco/useTitulo'

type Carga =
  | { tipo: 'cargando' }
  | { tipo: 'ok'; ficha: FichaPublica; puedo: Elegibilidad }
  | { tipo: 'error'; mensaje: string }

function mensajeDe(e: unknown, porDefecto: string): string {
  return e instanceof ErrorApi ? e.mensaje : porDefecto
}

/**
 * CU-16 · Publicar reseña (API-47 y API-48), y CS-16 · editar dentro de las 72 h (API-49).
 *
 * El orden del caso de uso es el orden de la pantalla: PRIMERO se pregunta si esta persona puede
 * reseñar a esta otra (paso 1) y solo entonces aparece el formulario. Un formulario que se pinta
 * siempre y falla al enviar le hace escribir a alguien un texto que nunca se va a publicar.
 */
export function DejarResena() {
  const { slug = '' } = useParams()
  const { estado: sesion } = useSesion()
  const [carga, setCarga] = useState<Carga>({ tipo: 'cargando' })
  const [intento, setIntento] = useState(0)

  useTitulo('Dejar una reseña')

  useEffect(() => {
    if (sesion !== 'dentro') return
    let vigente = true
    fichaPublica(slug)
      .then(async (ficha) => ({ ficha, puedo: await elegibilidad(ficha.id) }))
      .then(({ ficha, puedo }) => { if (vigente) setCarga({ tipo: 'ok', ficha, puedo }) })
      .catch((e: unknown) => {
        if (vigente) setCarga({ tipo: 'error', mensaje: mensajeDe(e, 'No pudimos cargar esta página.') })
      })
    return () => { vigente = false }
  }, [slug, sesion, intento])

  const recargar = useCallback(() => {
    setCarga({ tipo: 'cargando' })
    setIntento((n) => n + 1)
  }, [])

  if (sesion === 'cargando') {
    return <div className="contenedor ficha-pagina"><p className="cargando-linea" role="status">Cargando…</p></div>
  }
  if (sesion === 'fuera') {
    return <Navigate to="/entrar" replace state={{ despues: `/prestadores/${slug}/resena` }} />
  }
  if (carga.tipo === 'cargando') {
    return <div className="contenedor ficha-pagina"><p className="cargando-linea" role="status">Cargando…</p></div>
  }
  if (carga.tipo === 'error') {
    return (
      <div className="contenedor ficha-pagina">
        <p className="aviso-linea" role="alert">
          {carga.mensaje}{' '}
          <button type="button" className="enlace" onClick={recargar}>Reintentar</button>
        </p>
      </div>
    )
  }

  const { ficha, puedo } = carga
  return (
    <div className="contenedor ficha-pagina resenas-pagina">
      <Link className="volver" to={`/prestadores/${slug}`}>Volver a la ficha de {ficha.nombrePublico}</Link>
      <h1 className="pagina-titulo">Tu reseña de {ficha.nombrePublico}</h1>

      {puedo.elegible && puedo.contactoId !== null
        ? <Publicar contactoId={puedo.contactoId} slug={slug} nombre={ficha.nombrePublico} />
        : <NoElegible puedo={puedo} perfilId={ficha.id} nombre={ficha.nombrePublico} alRegistrar={recargar} />}
    </div>
  )
}

/**
 * CU-16 4A.3 y 4B.2: no basta con decir que no. Hay que decir QUE falta y, cuando hay una fecha,
 * desde cuando se podra. El texto viene de MOD-11, que es quien conoce la regla; la pantalla solo
 * decide que OFRECER en cada caso.
 */
function NoElegible({ puedo, perfilId, nombre, alRegistrar }: {
  puedo: Elegibilidad
  perfilId: string
  nombre: string
  alRegistrar: () => void
}) {
  const [registrando, setRegistrando] = useState(false)
  const [fallo, setFallo] = useState<string | null>(null)

  async function yaMeAtendio() {
    if (registrando) return
    setRegistrando(true); setFallo(null)
    try {
      // API-46: deja constancia del contacto y arranca los 7 dias. NO habilita la reseña hoy, y
      // la pantalla lo dice antes de que nadie pulse.
      await registrarContacto(perfilId, 'Ficha', 'Formulario')
      alRegistrar()
    } catch (e) {
      setFallo(mensajeDe(e, 'No pudimos registrar tu contacto. Intenta de nuevo.'))
    } finally {
      setRegistrando(false)
    }
  }

  return (
    <section className="bloque resena-no-elegible" aria-labelledby="no-elegible">
      <h2 id="no-elegible">Todavía no puedes reseñar</h2>
      <p className="bloque-texto">
        {puedo.mensaje ?? 'Por ahora no puedes dejar una reseña en este perfil.'}
      </p>
      {puedo.puedeDesde !== null && (
        <p className="bloque-texto letra-chica">Podrás hacerlo desde el {diaDeChile(puedo.puedeDesde)}.</p>
      )}

      {puedo.motivo === 'SinContacto' && (
        <div className="resena-contacto-directo">
          <p className="bloque-texto">
            Si {nombre.split(' ')[0]} ya te atendió fuera de ZASS! —por teléfono, por WhatsApp o en
            persona— déjalo registrado. Pasados 7 días podrás contar cómo te fue.
          </p>
          <button
            type="button" className="boton boton--amarillo boton--lleno" disabled={registrando}
            onClick={() => { void yaMeAtendio() }}
          >
            {registrando ? 'Registrando…' : 'Ya me atendió'}
          </button>
          {fallo !== null && <p className="aviso-linea" role="alert">{fallo}</p>}
        </div>
      )}
    </section>
  )
}

type Envio =
  | { tipo: 'escribiendo' }
  | { tipo: 'enviando' }
  | { tipo: 'avisa'; aviso: AvisoDeDatosPersonales }
  | { tipo: 'fallo'; mensaje: string }

/** Pasos 3 a 15 del flujo principal. */
function Publicar({ contactoId, slug, nombre }: { contactoId: string; slug: string; nombre: string }) {
  const [borrador, setBorrador] = useState<Borrador>(BORRADOR_VACIO)
  const [envio, setEnvio] = useState<Envio>({ tipo: 'escribiendo' })
  const [resena, setResena] = useState<Resena | null>(null)
  // Una clave por INTENCION (CC-04): nace con el formulario, no en el clic. Un doble clic, o un
  // reintento tras un corte de red, reusa la misma y el servidor devuelve la reseña ya creada en
  // vez de publicar una segunda.
  const [clave] = useState(nuevaClaveDeIdempotencia)

  async function enviar(enviarIgual: boolean) {
    if (envio.tipo === 'enviando' || borrador.calificacion === null) return
    setEnvio({ tipo: 'enviando' })
    try {
      const r = await publicar(
        contactoId, borrador.calificacion, borrador.comentario.trim(), borrador.aspectos,
        clave, enviarIgual)
      if (r.tipo === 'datos-personales') setEnvio({ tipo: 'avisa', aviso: r.aviso })
      else { setResena(r.resena); setEnvio({ tipo: 'escribiendo' }) }
    } catch (e) {
      setEnvio({ tipo: 'fallo', mensaje: mensajeDe(e, 'No pudimos publicar tu reseña. Intenta de nuevo.') })
    }
  }

  if (resena !== null) {
    return <Publicada resena={resena} slug={slug} nombre={nombre} alEditar={setResena} />
  }

  return (
    <section className="bloque" aria-labelledby="resena-nueva">
      <h2 id="resena-nueva">Cuéntale al próximo cómo te fue</h2>
      <p className="bloque-texto">
        Lo que escribas es público y queda con tu nombre de cuenta oculto. Podrás editarlo
        durante las 72 horas siguientes.
      </p>
      <FormularioDeResena
        borrador={borrador}
        alCambiar={setBorrador}
        alCorregir={() => setEnvio({ tipo: 'escribiendo' })}
        alEnviar={(igual) => { void enviar(igual) }}
        enviando={envio.tipo === 'enviando'}
        aviso={envio.tipo === 'avisa' ? envio.aviso : null}
        fallo={envio.tipo === 'fallo' ? envio.mensaje : null}
        etiquetaEnviar="Publicar mi reseña"
      />
    </section>
  )
}

/** Paso 15: la reseña publicada y el plazo que queda para cambiarla (CS-16). */
function Publicada({ resena, slug, nombre, alEditar }: {
  resena: Resena
  slug: string
  nombre: string
  alEditar: (resena: Resena) => void
}) {
  const [editando, setEditando] = useState(false)
  const [borrador, setBorrador] = useState<Borrador>({
    calificacion: resena.calificacion, comentario: resena.comentario, aspectos: [...resena.aspectos],
  })
  const [envio, setEnvio] = useState<Envio>({ tipo: 'escribiendo' })

  const queda = plazoDeEdicion(resena.editableHasta)

  async function guardar(enviarIgual: boolean) {
    if (envio.tipo === 'enviando' || borrador.calificacion === null) return
    setEnvio({ tipo: 'enviando' })
    try {
      const r = await editar(
        resena.id, borrador.calificacion, borrador.comentario.trim(), borrador.aspectos, enviarIgual)
      if (r.tipo === 'datos-personales') setEnvio({ tipo: 'avisa', aviso: r.aviso })
      else { alEditar(r.resena); setEditando(false); setEnvio({ tipo: 'escribiendo' }) }
    } catch (e) {
      setEnvio({ tipo: 'fallo', mensaje: mensajeDe(e, 'No pudimos guardar tu cambio. Intenta de nuevo.') })
    }
  }

  if (editando) {
    return (
      <section className="bloque" aria-labelledby="resena-editar">
        <h2 id="resena-editar">Editar tu reseña</h2>
        <FormularioDeResena
          borrador={borrador}
          alCambiar={setBorrador}
          alCorregir={() => setEnvio({ tipo: 'escribiendo' })}
          alEnviar={(igual) => { void guardar(igual) }}
          enviando={envio.tipo === 'enviando'}
          aviso={envio.tipo === 'avisa' ? envio.aviso : null}
          fallo={envio.tipo === 'fallo' ? envio.mensaje : null}
          etiquetaEnviar="Guardar los cambios"
          alCancelar={() => { setEditando(false); setEnvio({ tipo: 'escribiendo' }) }}
        />
      </section>
    )
  }

  return (
    <section className="bloque resena-listo" aria-labelledby="resena-listo-titulo">
      <h2 id="resena-listo-titulo">Tu reseña está publicada</h2>
      <article className="resena">
        <Estrellas valor={resena.calificacion} />
        <p className="texto-largo">{resena.comentario}</p>
      </article>

      {/* 4D.5: pasado el plazo el boton no aparece. Dejarlo a la vista para que el servidor
          responda que no se puede seria prometer algo que ya no existe. */}
      {queda !== null ? (
        <>
          <p className="bloque-texto">Puedes cambiarla durante {queda} más.</p>
          <button type="button" className="boton boton--chico" onClick={() => setEditando(true)}>
            Editar mi reseña
          </button>
        </>
      ) : (
        <p className="bloque-texto letra-chica">El plazo de 72 horas para editarla ya terminó.</p>
      )}

      <p className="bloque-texto">
        <Link to={`/prestadores/${slug}/resenas`}>Ver lo que dicen de {nombre}</Link>
      </p>
    </section>
  )
}
