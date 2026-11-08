import { useEffect, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import { fichaPublica, type FichaPublica } from '../funciones/busqueda/servicio'
import { Icono } from '../marco/Icono'
import { Monograma } from '../marco/Monograma'
import { capitalizar, duracion, etiquetaDeTipo, pesos, reputacion } from '../marco/texto'
import { descripcionDeFicha } from '../marco/seo'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'
import { useSesion } from '../sesion/contexto'
import { AgendarHora } from './AgendarHora'
import { NoEncontrada } from './NoEncontrada'

/** Solo http(s): un `javascript:` en el sitio web de alguien no se vuelve un enlace. */
function enlaceSeguro(url: string): string | null {
  return /^https?:\/\//i.test(url) ? url : null
}

type Estado =
  | { slug: string; tipo: 'ok'; ficha: FichaPublica }
  | { slug: string; tipo: 'no-existe' }
  | { slug: string; tipo: 'error'; mensaje: string }

/**
 * La pieza central del sitio: una persona, como la ve cualquiera. Todo lo que sale aqui es
 * lo que el propio prestador eligio mostrar (la API no manda nada mas), y lo que aun no existe
 * —las reseñas escritas— se dice con claridad en vez de rellenarse.
 */
export function Ficha() {
  const { slug = '' } = useParams()
  // Flujo 4G de CU-12: quien entro o se registro vuelve con el servicio y la hora que habia elegido.
  const [busqueda] = useSearchParams()
  const [estado, setEstado] = useState<Estado | null>(null)
  const [intento, setIntento] = useState(0)
  // El contacto depende de la sesion: al entrar o salir hay que pedir la ficha otra vez, o se quedaria el de antes.
  const { estado: estadoSesion } = useSesion()

  useEffect(() => {
    let vigente = true
    fichaPublica(slug)
      .then((ficha) => { if (vigente) setEstado({ slug, tipo: 'ok', ficha }) })
      .catch((e: unknown) => {
        if (!vigente) return
        if (e instanceof ErrorApi && e.estado === 404) setEstado({ slug, tipo: 'no-existe' })
        else setEstado({
          slug, tipo: 'error',
          mensaje: e instanceof ErrorApi ? e.mensaje : 'No pudimos cargar esta ficha.',
        })
      })
    return () => { vigente = false }
  }, [slug, intento, estadoSesion])

  // Un estado de otra ficha (acabas de navegar) cuenta como "cargando", no como la tuya.
  const actual = estado !== null && estado.slug === slug ? estado : null
  const ficha = actual?.tipo === 'ok' ? actual.ficha : null

  useTitulo(ficha ? ficha.nombrePublico : actual?.tipo === 'no-existe' ? 'Ficha no encontrada' : 'Ficha')
  // Al navegar dentro de la app el servidor no interviene: la misma descripcion que pone la Function de la ficha.
  useMeta({
    descripcion: ficha ? descripcionDeFicha(ficha) : undefined,
    noindex: actual?.tipo === 'no-existe',
  })

  if (actual?.tipo === 'no-existe') {
    return (
      <NoEncontrada
        titulo="Esta ficha no existe, o ya no está publicada."
        texto="Puede que la persona la haya pausado. Busca a alguien más de su oficio."
      />
    )
  }

  if (actual?.tipo === 'error') {
    return (
      <div className="contenedor ficha-pagina">
        <p className="aviso-linea" role="alert">
          {actual.mensaje}{' '}
          <button
            type="button" className="enlace"
            onClick={() => { setEstado(null); setIntento((n) => n + 1) }}
          >
            Reintentar
          </button>
        </p>
      </div>
    )
  }

  if (ficha === null) {
    return (
      <div className="contenedor ficha-pagina">
        <p className="cargando-linea" role="status">Cargando la ficha…</p>
      </div>
    )
  }

  const principal = ficha.zonas.find((z) => z.esPrincipal)
  const c = ficha.contacto
  const sitio = c.sitioWeb ? enlaceSeguro(c.sitioWeb) : null
  const wasap = c.whatsapp ? c.whatsapp.replace(/\D/g, '') : ''
  const hayContacto = Boolean(c.telefono || wasap || c.correo || sitio)

  return (
    <div className="contenedor ficha-pagina">
      <Link to="/buscar" className="volver">
        <Icono nombre="flecha" />Seguir buscando
      </Link>

      <div className="ficha-rejilla">
        <article>
          <header className="persona">
            <Monograma nombre={ficha.nombrePublico} tamano="g" />
            <div>
              <h1>{ficha.nombrePublico}</h1>
              <p className="persona-detalle">
                {[ficha.rubro?.nombre && capitalizar(ficha.rubro.nombre), principal?.nombre]
                  .filter(Boolean).join(' · ')}
              </p>
              <div className="fila-etiquetas">
                {ficha.totalResenas > 0 && (
                  <span className="dato">
                    <Icono nombre="estrella" />{reputacion(ficha.promedioCalificacion, ficha.totalResenas)}
                  </span>
                )}
              </div>
            </div>
          </header>

          <section className="bloque">
            <h2>Sobre {ficha.nombrePublico.split(' ')[0]}</h2>
            {ficha.descripcion ? (
              <p className="bloque-texto texto-largo">{ficha.descripcion}</p>
            ) : (
              <p className="bloque-texto bloque-vacio">Todavía no escribió nada sobre sí.</p>
            )}
          </section>

          <section className="bloque">
            <h2>Qué ofrece</h2>
            {ficha.servicios.length > 0 ? (
              <ul className="servicios">
                {ficha.servicios.map((s) => (
                  <li key={s.nombre} className="servicio">
                    <strong>{s.nombre}</strong>
                    <span>
                      {duracion(s.duracionMinutos)}
                      {s.precioReferencia !== null && ` · desde ${pesos(s.precioReferencia)}`}
                    </span>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="bloque-texto bloque-vacio">Todavía no publicó sus servicios.</p>
            )}
          </section>

          <AgendarHora
            slug={ficha.slug}
            nombre={ficha.nombrePublico}
            servicios={ficha.servicios}
            elegidoInicial={{ servicio: busqueda.get('servicio'), bloque: busqueda.get('bloque') }}
          />

          {ficha.credenciales.length > 0 && (
            <section className="bloque">
              <h2>Credenciales</h2>
              <ul className="credenciales">
                {ficha.credenciales.map((c) => (
                  <li key={`${c.nombre}-${c.obtenidaEn}`}>
                    <Icono nombre="sello" />
                    <div>
                      <strong>{c.nombre}</strong>
                      <small>{[etiquetaDeTipo(c.tipo), c.entidad, c.obtenidaEn.slice(0, 4)].filter(Boolean).join(' · ')}</small>
                      <span className={c.verificada ? 'sello' : 'dato'}>{c.etiqueta}</span>
                    </div>
                  </li>
                ))}
              </ul>
            </section>
          )}

          <section className="bloque">
            <h2>Dónde atiende</h2>
            {ficha.zonas.length > 0 ? (
              <ul className="zonas">
                {ficha.zonas.map((z) => (
                  <li key={z.nombre} className={z.esPrincipal ? 'zona zona--principal' : 'zona'}>
                    <Icono nombre="pin" />{z.nombre}
                  </li>
                ))}
              </ul>
            ) : (
              <p className="bloque-texto bloque-vacio">Todavía no indicó dónde atiende.</p>
            )}
          </section>

          <section className="bloque">
            <h2>Lo que dicen de {ficha.nombrePublico.split(' ')[0]}</h2>
            {ficha.totalResenas > 0 ? (
              <p className="nota">
                {ficha.promedioCalificacion !== null && (
                  <span className="nota-promedio">
                    {ficha.promedioCalificacion.toFixed(1).replace('.', ',')}
                  </span>
                )}
                <span className="nota-detalle">
                  {ficha.promedioCalificacion !== null
                    ? `de 5, según ${ficha.totalResenas} ${ficha.totalResenas === 1 ? 'reseña' : 'reseñas'}`
                    : `${ficha.totalResenas} ${ficha.totalResenas === 1 ? 'reseña' : 'reseñas'}. El promedio se muestra desde la tercera.`}
                </span>
              </p>
            ) : (
              <p className="bloque-texto bloque-vacio">Todavía no tiene reseñas.</p>
            )}
          </section>
        </article>

        <aside className="lateral" aria-label="Contacto">
          <div className="lateral-caja">
            <h2>Hablar con {ficha.nombrePublico.split(' ')[0]}</h2>
            {ficha.contactoRequiereCuenta ? (
              <div className="contacto-cuenta">
                <p>
                  Para hablar con {ficha.nombrePublico.split(' ')[0]} crea tu cuenta gratis.
                  Toma un minuto y a quien busca nunca se le cobra.
                </p>
                <Link className="boton boton--amarillo boton--lleno" to="/crear-cuenta" state={{ despues: `/prestadores/${slug}` }}>
                  <Icono nombre="mensaje" />Crear mi cuenta gratis
                </Link>
                <p className="letra-chica">
                  ¿Ya tienes cuenta? <Link to="/entrar" state={{ despues: `/prestadores/${slug}` }}>Entrar</Link>
                </p>
              </div>
            ) : hayContacto ? (
              <ul className="contacto">
                {wasap && (
                  <li>
                    <a className="boton boton--amarillo boton--lleno" href={`https://wa.me/${wasap}`} rel="noreferrer noopener" target="_blank">
                      <Icono nombre="mensaje" />Escribir por WhatsApp
                    </a>
                  </li>
                )}
                {c.telefono && (
                  <li>
                    <a className="boton boton--lleno" href={`tel:${c.telefono.replace(/[^\d+]/g, '')}`}>
                      <Icono nombre="telefono" />Llamar · {c.telefono}
                    </a>
                  </li>
                )}
                {c.correo && (
                  <li>
                    <a className="boton boton--lleno" href={`mailto:${c.correo}`}>
                      <Icono nombre="correo" />Escribir un correo
                    </a>
                  </li>
                )}
                {sitio && (
                  <li>
                    <a className="boton boton--lleno" href={sitio} rel="noreferrer noopener" target="_blank">
                      <Icono nombre="mundo" />Ver su sitio web
                    </a>
                  </li>
                )}
              </ul>
            ) : (
              <p className="bloque-vacio">
                Esta persona todavía no publicó cómo contactarla.
              </p>
            )}
            <p className="letra-chica">
              Contactar no te cuesta nada: hablas directo con quien te atiende.
            </p>
          </div>
        </aside>
      </div>
    </div>
  )
}
