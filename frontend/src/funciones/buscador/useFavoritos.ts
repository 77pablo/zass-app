import { useCallback, useEffect, useState } from 'react'
import { ErrorApi } from '../../api/cliente'
import { useSesion } from '../../sesion/contexto'
import { guardarFavorito, miPerfilBuscador, quitarFavorito } from './servicio'

/** De quien son los identificadores cargados: al cambiar de cuenta no se muestran los de la anterior. */
interface Cargados { usuarioId: string; ids: Set<string> }

export interface Favoritos {
  /** Si esa persona esta guardada. `null` mientras no se sabe (cargando, o sin sesion). */
  guardado: (perfilId: string) => boolean | null
  /** Guardar o quitar. Cambia al instante y vuelve atras si la API dice que no. */
  alternar: (perfilId: string) => Promise<void>
  /** Las que estan yendo y viniendo de la API: el boton se desactiva mientras. */
  enCurso: (perfilId: string) => boolean
  /** Por que no se pudo, en palabras para la persona. Nulo si todo fue bien. */
  error: string | null
}

/**
 * CS-08. Los favoritos de quien tiene sesion, para pintar el boton de guardar en la ficha y en
 * los resultados. Pide SOLO los identificadores (API-23b): las tarjetas completas son de la
 * pantalla de guardados.
 */
export function useFavoritos(): Favoritos {
  const { estado, usuario } = useSesion()
  const usuarioId = estado === 'dentro' ? usuario?.id ?? null : null
  const [cargados, setCargados] = useState<Cargados | null>(null)
  const [pendientes, setPendientes] = useState<ReadonlySet<string>>(new Set())
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (usuarioId === null) return
    let vigente = true
    miPerfilBuscador()
      .then((p) => { if (vigente) setCargados({ usuarioId, ids: new Set(p.favoritos) }) })
      // Si no se pudo leer, el boton no se muestra como «guardado» por error: queda sin estado.
      .catch(() => {})
    return () => { vigente = false }
  }, [usuarioId])

  const ids = cargados !== null && cargados.usuarioId === usuarioId ? cargados.ids : null

  const guardado = useCallback((perfilId: string) => (ids === null ? null : ids.has(perfilId)), [ids])
  const enCurso = useCallback((perfilId: string) => pendientes.has(perfilId), [pendientes])

  const alternar = useCallback(async (perfilId: string) => {
    if (ids === null || usuarioId === null || pendientes.has(perfilId)) return
    const estaba = ids.has(perfilId)
    const cambiar = (poner: boolean) => setCargados((c) => {
      if (c === null) return c
      const siguiente = new Set(c.ids)
      if (poner) siguiente.add(perfilId)
      else siguiente.delete(perfilId)
      return { ...c, ids: siguiente }
    })

    setError(null)
    setPendientes((p) => new Set(p).add(perfilId))
    cambiar(!estaba)
    try {
      await (estaba ? quitarFavorito(perfilId) : guardarFavorito(perfilId))
    } catch (e) {
      cambiar(estaba)
      setError(e instanceof ErrorApi && e.estado !== 0 && e.estado < 500
        ? e.mensaje
        : estaba ? 'No pudimos quitarlo de tus guardados. Intenta de nuevo.' : 'No pudimos guardarlo. Intenta de nuevo.')
    } finally {
      setPendientes((p) => { const s = new Set(p); s.delete(perfilId); return s })
    }
  }, [ids, usuarioId, pendientes])

  return { guardado, alternar, enCurso, error }
}
