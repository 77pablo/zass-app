import { useEffect, useState } from 'react'
import { ErrorApi } from './cliente'
import { listarRubros, listarZonas, type Rubro, type Zona } from './catalogo'
import { resumenDeComunidad, type ResumenDeComunidad } from './comunidad'

export interface Cargado<T> {
  datos: T | null
  error: string | null
  cargando: boolean
  reintentar: () => void
}

/**
 * Carga algo al montar y deja reintentar. `cargar` tiene que ser estable (una funcion
 * de modulo), porque cambiarla vuelve a pedir los datos.
 *
 * La bandera `vigente` descarta la respuesta de un efecto que ya fue reemplazado o desmontado:
 * con StrictMode el efecto corre dos veces en desarrollo, y sin ella la primera respuesta
 * pisaria a la segunda.
 */
export function useCargado<T>(cargar: () => Promise<T>, aviso: string): Cargado<T> {
  const [estado, setEstado] = useState<{ datos: T | null; error: string | null }>({
    datos: null, error: null,
  })
  const [intento, setIntento] = useState(0)

  useEffect(() => {
    let vigente = true
    cargar()
      .then((datos) => { if (vigente) setEstado({ datos, error: null }) })
      .catch((e: unknown) => {
        if (vigente) setEstado({ datos: null, error: e instanceof ErrorApi ? e.mensaje : aviso })
      })
    return () => { vigente = false }
  }, [cargar, aviso, intento])

  return {
    ...estado,
    cargando: estado.datos === null && estado.error === null,
    reintentar: () => {
      setEstado({ datos: null, error: null })
      setIntento((n) => n + 1)
    },
  }
}

type Catalogo = { rubros: Rubro[]; zonas: Zona[] }

// El catalogo casi no cambia (oficios y barrios), y cada pagina lo necesita: se pide una vez por
// carga del sitio. Si falla no se guarda, para que reintentar de verdad vuelva a preguntar.
let catalogoGuardado: Promise<Catalogo> | null = null

function catalogo(): Promise<Catalogo> {
  catalogoGuardado ??= Promise.all([listarRubros(), listarZonas()])
    .then(([rubros, zonas]) => ({ rubros, zonas }))
    .catch((e: unknown) => { catalogoGuardado = null; throw e })
  return catalogoGuardado
}

// La comunidad si cambia (alguien se publica), pero no cada segundo: 60 s alcanzan para que
// ir de la portada a los barrios y volver no repita la pregunta.
const VIGENCIA_COMUNIDAD_MS = 60_000
let comunidadGuardada: { hasta: number; promesa: Promise<ResumenDeComunidad> } | null = null

function comunidad(): Promise<ResumenDeComunidad> {
  if (comunidadGuardada === null || comunidadGuardada.hasta < Date.now()) {
    const promesa = resumenDeComunidad().catch((e: unknown) => { comunidadGuardada = null; throw e })
    comunidadGuardada = { hasta: Date.now() + VIGENCIA_COMUNIDAD_MS, promesa }
  }
  return comunidadGuardada.promesa
}

/** Para las pruebas: cada una parte sin lo que dejo la anterior. */
export function olvidarCaches(): void {
  catalogoGuardado = null
  comunidadGuardada = null
}

/** Rubros y zonas: lo necesitan la portada, la busqueda, los barrios y los oficios. */
export const useCatalogo = () =>
  useCargado(catalogo, 'No pudimos cargar los oficios y los barrios.')

/** API-62. Cuanta gente hay y quien llego. Si falla, las paginas siguen sin los numeros. */
export const useComunidad = (): Cargado<ResumenDeComunidad> =>
  useCargado(comunidad, 'No pudimos cargar la comunidad.')
