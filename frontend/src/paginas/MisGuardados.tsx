import { useEffect, useMemo, useState } from 'react'
import { Link, Navigate } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import { useCatalogo } from '../api/useCargado'
import {
  guardarPreferencias, MAX_RUBROS_DE_INTERES, miPerfilBuscador, misFavoritos, quitarFavorito,
  type Favorito, type MiPerfilBuscador,
} from '../funciones/buscador/servicio'
import { Icono } from '../marco/Icono'
import { PaginaCargando } from '../marco/PaginaCargando'
import { TarjetaPersona } from '../marco/TarjetaPersona'
import { capitalizar, reputacion } from '../marco/texto'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'
import { agruparPorRegion } from '../marco/zonas'
import { useSesion } from '../sesion/contexto'

const RUTA = '/mis-guardados'
const TITULO = 'Tus guardados'

function mensajeDe(e: unknown, porDefecto: string): string {
  return e instanceof ErrorApi && e.estado !== 0 && e.estado < 500 ? e.mensaje : porDefecto
}

/**
 * CS-08 y CU-04. Lo que la persona guardo: la gente que le sirvio (API-23-favoritos) y lo que
 * suele buscar (API-23b), que la portada usa como punto de partida.
 *
 * Las tarjetas son las mismas de la busqueda y NO traen contacto: para hablar con alguien se
 * entra a su ficha, que es donde el contacto se muestra con cuenta verificada (RN-S1-09).
 */
export function MisGuardados() {
  useMeta({ noindex: true })
  useTitulo(TITULO)
  const { estado } = useSesion()

  const [favoritos, setFavoritos] = useState<Favorito[] | null>(null)
  const [perfil, setPerfil] = useState<MiPerfilBuscador | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [intento, setIntento] = useState(0)

  useEffect(() => {
    if (estado !== 'dentro') return
    let vigente = true
    // Una tras otra y no en paralelo no hace falta: son dos lecturas independientes.
    Promise.all([misFavoritos(), miPerfilBuscador()])
      .then(([lista, mio]) => { if (vigente) { setFavoritos(lista); setPerfil(mio); setError(null) } })
      .catch((e: unknown) => { if (vigente) setError(mensajeDe(e, 'No pudimos cargar tus guardados.')) })
    return () => { vigente = false }
  }, [estado, intento])

  if (estado === 'cargando') return <PaginaCargando titulo={TITULO} />
  if (estado === 'fuera') return <Navigate to="/entrar" replace state={{ despues: RUTA }} />

  if (error !== null && favoritos === null) {
    return (
      <div className="contenedor ficha-pagina">
        <h1 className="pagina-titulo">{TITULO}</h1>
        <p className="aviso-linea" role="alert">
          {error}{' '}
          <button
            type="button" className="enlace"
            onClick={() => { setError(null); setIntento((n) => n + 1) }}
          >
            Reintentar
          </button>
        </p>
      </div>
    )
  }
  if (favoritos === null || perfil === null) {
    return <PaginaCargando titulo={TITULO} aviso="Cargando tus guardados…" />
  }

  return (
    <div className="contenedor ficha-pagina">
      <h1 className="pagina-titulo">{TITULO}</h1>
      <ListaDeGuardados favoritos={favoritos} alCambiar={setFavoritos} />
      <Preferencias perfil={perfil} alGuardar={setPerfil} />
    </div>
  )
}

function ListaDeGuardados({ favoritos, alCambiar }: {
  favoritos: Favorito[]
  alCambiar: (lista: Favorito[]) => void
}) {
  const [quitando, setQuitando] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function quitar(f: Favorito) {
    setQuitando(f.perfilId)
    setError(null)
    try {
      await quitarFavorito(f.perfilId)
      alCambiar(favoritos.filter((x) => x.perfilId !== f.perfilId))
    } catch (e) {
      setError(mensajeDe(e, `No pudimos quitar a ${f.nombrePublico}. Intenta de nuevo.`))
    } finally {
      setQuitando(null)
    }
  }

  return (
    <section className="bloque" aria-labelledby="guardados-gente">
      <h2 id="guardados-gente">Gente que guardaste</h2>
      {error && <p className="aviso-linea" role="alert">{error}</p>}
      {favoritos.length === 0 ? (
        <p className="bloque-texto bloque-vacio">
          Todavía no guardas a nadie. Cuando veas a alguien que te sirva, toca «Guardar» en su
          ficha y lo vas a encontrar aquí. <Link to="/buscar">Buscar a alguien</Link>.
        </p>
      ) : (
        <>
          <p className="bloque-texto">
            Para hablar con alguien, entra a su ficha: ahí está su contacto.
          </p>
          <ul className="tarjetas-rejilla" aria-label="Gente que guardaste">
            {favoritos.map((f) => (
              <li key={f.perfilId}>
                <TarjetaPersona p={{
                  slug: f.slug,
                  nombre: f.nombrePublico,
                  rubro: f.rubro ? capitalizar(f.rubro) : null,
                  zona: f.zonaPrincipal,
                  verificada: f.tieneCredencialVerificada,
                  detalle: f.totalResenas > 0 ? reputacion(f.promedioCalificacion, f.totalResenas) : null,
                  foto: f.urlFoto,
                }} />
                <button
                  type="button" className="boton boton--chico"
                  disabled={quitando === f.perfilId}
                  onClick={() => void quitar(f)}
                >
                  <Icono nombre="cerrar" />{quitando === f.perfilId ? 'Quitando…' : 'Quitar'}{' '}
                  <span className="solo-lector">a {f.nombrePublico} de tus guardados</span>
                </button>
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  )
}

/**
 * CU-04. La comuna y los oficios con que la portada parte. Se guardan juntos con un boton, no al
 * tocar cada uno: asi la persona elige con calma y un toque por error no cambia nada.
 */
function Preferencias({ perfil, alGuardar }: {
  perfil: MiPerfilBuscador
  alGuardar: (p: MiPerfilBuscador) => void
}) {
  const catalogo = useCatalogo()
  const [zona, setZona] = useState(perfil.zonaInteresId === null ? '' : String(perfil.zonaInteresId))
  const [rubros, setRubros] = useState<number[]>(perfil.rubrosInteres)
  const [envio, setEnvio] = useState<{ tipo: 'quieto' | 'enviando' | 'listo' } | { tipo: 'fallo'; mensaje: string }>({ tipo: 'quieto' })

  const grupos = useMemo(() => agruparPorRegion(catalogo.datos?.zonas ?? []), [catalogo.datos])
  const lleno = rubros.length >= MAX_RUBROS_DE_INTERES

  function alternarRubro(id: number) {
    setEnvio({ tipo: 'quieto' })
    setRubros((r) => (r.includes(id) ? r.filter((x) => x !== id) : r.length >= MAX_RUBROS_DE_INTERES ? r : [...r, id]))
  }

  async function guardar() {
    setEnvio({ tipo: 'enviando' })
    const zonaInteresId = zona === '' ? null : Number(zona)
    try {
      // Lo demas (radio, avisos, canales) va tal cual estaba: esta pantalla no lo cambia.
      await guardarPreferencias({
        zonaInteresId, radioPreferidoKm: perfil.radioPreferidoKm, aceptaAvisos: perfil.aceptaAvisos,
        canalCorreo: perfil.canalCorreo, canalPush: perfil.canalPush, rubrosInteres: rubros,
      })
      alGuardar({ ...perfil, zonaInteresId, rubrosInteres: rubros })
      setEnvio({ tipo: 'listo' })
    } catch (e) {
      setEnvio({ tipo: 'fallo', mensaje: mensajeDe(e, 'No pudimos guardar tus preferencias. Intenta de nuevo.') })
    }
  }

  return (
    <section className="bloque" aria-labelledby="guardados-busqueda">
      <h2 id="guardados-busqueda">Lo que sueles buscar</h2>
      <p className="bloque-texto">
        Elige tu comuna y hasta {MAX_RUBROS_DE_INTERES} oficios. Te los mostramos primero en la portada.
      </p>

      {catalogo.error && (
        <p className="aviso-linea" role="alert">
          {catalogo.error}{' '}
          <button type="button" className="enlace" onClick={catalogo.reintentar}>Reintentar</button>
        </p>
      )}

      <div className="campo">
        <label className="campo-etiqueta" htmlFor="pref-comuna">Tu comuna</label>
        <select
          id="pref-comuna" className="campo-entrada" value={zona}
          onChange={(e) => { setZona(e.target.value); setEnvio({ tipo: 'quieto' }) }}
        >
          <option value="">Sin comuna (todo Chile)</option>
          {grupos.map((g) => (
            <optgroup key={g.region} label={g.region}>
              {g.comunas.flatMap(({ comuna, barrios }) => [
                <option key={comuna.id} value={comuna.id}>{comuna.nombre}</option>,
                ...barrios.map((b) => <option key={b.id} value={b.id}>{`${comuna.nombre} · ${b.nombre}`}</option>),
              ])}
            </optgroup>
          ))}
        </select>
      </div>

      <div className="campo">
        <p className="campo-etiqueta" id="pref-oficios">Oficios que te interesan</p>
        <div className="chips" role="group" aria-labelledby="pref-oficios">
          {(catalogo.datos?.rubros ?? []).map((r) => {
            const elegido = rubros.includes(r.id)
            return (
              <button
                key={r.id} type="button" className="chip" aria-pressed={elegido}
                disabled={!elegido && lleno}
                onClick={() => alternarRubro(r.id)}
              >
                {capitalizar(r.nombre)}
              </button>
            )
          })}
        </div>
        {lleno && (
          <p className="letra-chica" role="status">
            Ya elegiste {MAX_RUBROS_DE_INTERES}. Para elegir otro, quita uno.
          </p>
        )}
      </div>

      <p className="seccion-cierre">
        <button
          type="button" className="boton boton--lleno"
          disabled={envio.tipo === 'enviando'}
          onClick={() => void guardar()}
        >
          {envio.tipo === 'enviando' ? 'Guardando…' : 'Guardar lo que busco'}
        </button>
      </p>
      {envio.tipo === 'listo' && (
        <p className="letra-chica" role="status">
          Listo. <Link to="/">Ver la portada</Link>
        </p>
      )}
      {envio.tipo === 'fallo' && <p className="aviso-linea" role="alert">{envio.mensaje}</p>}
    </section>
  )
}
