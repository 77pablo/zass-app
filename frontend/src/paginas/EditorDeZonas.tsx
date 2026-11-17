import { useMemo, useState, type FormEvent } from 'react'
import { ErrorApi } from '../api/cliente'
import type { Zona } from '../api/catalogo'
import { guardarCobertura, type ZonaCubierta } from '../funciones/perfil/servicio'
import { Icono } from '../marco/Icono'
import { sinTildes } from '../marco/texto'
import { agruparPorRegion, etiquetaDeZona } from '../marco/zonas'

/** El mismo tope que la API (ServicioGeografia.MaxZonas): la pantalla lo dice antes de que el servidor lo rechace. */
export const MAXIMO_DE_ZONAS = 10

/**
 * Donde atiende el prestador: una o varias comunas de Chile (y, en las ciudades que los tengan, barrios), con una
 * principal que es la que se muestra primero. Las 346 comunas se encuentran escribiendo, o abriendo su region.
 */
export function EditorDeZonas({
  zonas, actuales, alGuardar, alCancelar,
}: { zonas: Zona[]; actuales: ZonaCubierta[]; alGuardar: () => void; alCancelar: () => void }) {
  const [elegidas, setElegidas] = useState<number[]>(() => actuales.map((z) => z.zonaId))
  const [principal, setPrincipal] = useState<number | null>(() => actuales.find((z) => z.esPrincipal)?.zonaId ?? null)
  const [filtro, setFiltro] = useState('')
  const [aviso, setAviso] = useState<string>()
  const [guardando, setGuardando] = useState(false)

  const grupos = useMemo(() => agruparPorRegion(zonas), [zonas])
  const porId = useMemo(() => new Map(zonas.map((z) => [z.id, z])), [zonas])
  const buscado = sinTildes(filtro.trim())

  const visibles = useMemo(() => {
    if (buscado === '') return grupos
    return grupos
      .map((g) => ({
        ...g,
        comunas: g.comunas.filter((c) =>
          sinTildes(c.comuna.nombre).includes(buscado) || c.barrios.some((b) => sinTildes(b.nombre).includes(buscado))),
      }))
      .filter((g) => g.comunas.length > 0)
  }, [grupos, buscado])

  function alternar(id: number) {
    setAviso(undefined)
    if (elegidas.includes(id)) {
      setElegidas(elegidas.filter((e) => e !== id))
      // Si se quita la principal, deja de haberla: hay que elegir otra.
      if (principal === id) setPrincipal(null)
      return
    }
    if (elegidas.length >= MAXIMO_DE_ZONAS) {
      setAviso(`Puedes cubrir hasta ${MAXIMO_DE_ZONAS} comunas. Quita una para agregar otra.`)
      return
    }
    setElegidas([...elegidas, id])
    // La primera que se elige queda de principal: es lo que casi siempre se quiere, y se puede cambiar.
    if (elegidas.length === 0) setPrincipal(id)
  }

  async function enviar(e: FormEvent) {
    e.preventDefault()
    setAviso(undefined)
    if (elegidas.length === 0) { setAviso('Elige al menos una comuna.'); return }
    if (principal === null || !elegidas.includes(principal)) { setAviso('Marca cuál es tu comuna principal.'); return }

    setGuardando(true)
    try {
      await guardarCobertura(elegidas.map((zonaId) => ({ zonaId, esPrincipal: zonaId === principal })))
      alGuardar()
    } catch (err) {
      setAviso(err instanceof ErrorApi ? err.mensaje : 'No pudimos guardar tus comunas.')
    } finally {
      setGuardando(false)
    }
  }

  const llena = elegidas.length >= MAXIMO_DE_ZONAS

  return (
    <form className="editor" onSubmit={(e) => void enviar(e)} noValidate>
      {aviso && <p className="aviso-linea" role="alert">{aviso}</p>}

      <p className="campo-ayuda">
        Elige dónde atiendes (hasta {MAXIMO_DE_ZONAS}) y marca una como principal: es la que se muestra primero.
      </p>

      <section aria-label="Tus comunas" className="elegidas">
        <p className="elegidas-cuenta"><strong>{elegidas.length}</strong> de {MAXIMO_DE_ZONAS}</p>
        {elegidas.length === 0 ? (
          <p className="bloque-vacio">Todavía no elegiste ninguna.</p>
        ) : (
          <ul className="elegidas-lista">
            {elegidas.map((id) => {
              const z = porId.get(id)
              const nombre = z ? etiquetaDeZona(z) : `Zona ${id}`
              return (
                <li key={id} className={`elegida${principal === id ? ' elegida--principal' : ''}`}>
                  <span className="elegida-nombre">{nombre}</span>
                  <label className="casilla casilla--principal">
                    <input type="radio" name="principal" checked={principal === id} onChange={() => setPrincipal(id)} />
                    <span>Principal<span className="solo-lector">: {nombre}</span></span>
                  </label>
                  <button type="button" className="elegida-quitar" onClick={() => alternar(id)} aria-label={`Quitar ${nombre}`}>
                    <Icono nombre="cerrar" />
                  </button>
                </li>
              )
            })}
          </ul>
        )}
      </section>

      <div className="filtro-comunas filtro-comunas--editor">
        <label htmlFor="editor-filtro" className="solo-lector">Buscar una comuna</label>
        <Icono nombre="lupa" />
        <input
          id="editor-filtro" type="search" autoComplete="off" spellCheck={false} maxLength={60}
          placeholder="Busca una comuna…" value={filtro} onChange={(e) => setFiltro(e.target.value)}
        />
      </div>

      {visibles.length === 0 && <p className="vacio-linea" role="status">No hay ninguna comuna que se llame así.</p>}

      <div className="regiones">
        {visibles.map((g) => (
          <details
            key={g.region} className="region"
            open={buscado !== '' || g.comunas.some((c) => elegidas.includes(c.comuna.id) || c.barrios.some((b) => elegidas.includes(b.id)))}
          >
            <summary>
              <span className="region-nombre">{g.region}</span>
              <span className="region-cuenta">{g.comunas.length} comunas<i aria-hidden="true" /></span>
            </summary>
            <ul className="comunas-lista comunas-lista--editor">
              {g.comunas.map(({ comuna, barrios }) => (
                <li key={comuna.id}>
                  <label className="casilla">
                    <input
                      type="checkbox" checked={elegidas.includes(comuna.id)}
                      disabled={llena && !elegidas.includes(comuna.id)} onChange={() => alternar(comuna.id)}
                    />
                    <span>{comuna.nombre}</span>
                  </label>
                  {barrios.length > 0 && (
                    <ul className="barrios-lista" aria-label={`Barrios de ${comuna.nombre}`}>
                      {barrios.map((b) => (
                        <li key={b.id}>
                          <label className="casilla">
                            <input
                              type="checkbox" checked={elegidas.includes(b.id)} aria-label={etiquetaDeZona(b)}
                              disabled={llena && !elegidas.includes(b.id)} onChange={() => alternar(b.id)}
                            />
                            <span>{b.nombre}</span>
                          </label>
                        </li>
                      ))}
                    </ul>
                  )}
                </li>
              ))}
            </ul>
          </details>
        ))}
      </div>

      <div className="editor-acciones">
        <button className="boton boton--tinta" type="submit" disabled={guardando}>{guardando ? 'Guardando…' : 'Guardar comunas'}</button>
        <button className="boton" type="button" onClick={alCancelar} disabled={guardando}>Cancelar</button>
      </div>
    </form>
  )
}
