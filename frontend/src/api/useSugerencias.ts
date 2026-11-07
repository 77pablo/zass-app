import { useEffect, useState } from 'react'
import { buscarTexto, type PaginaDeTexto } from '../funciones/busqueda/servicio'

const ESPERA_MS = 280
const MINIMO = 2

// Lo ya preguntado en esta carga: volver a escribir lo mismo, o borrar una letra y ponerla de nuevo,
// no vuelve a la API. El limite publico es de 100 peticiones por 15 minutos por IP, y escribir es la
// forma mas facil de gastarlas.
const guardado = new Map<string, PaginaDeTexto>()
const MAXIMO_GUARDADO = 60

/** Para las pruebas: cada una parte sin lo que dejo la anterior. */
export function olvidarSugerencias(): void {
  guardado.clear()
}

export interface Sugerencias {
  /** Las primeras personas que coinciden con lo escrito. */
  personas: PaginaDeTexto['resultados']
  /** Cuantas coinciden en total. */
  total: number
  /** Hay texto suficiente y todavia no llega la respuesta de ESE texto. */
  cargando: boolean
  error: boolean
}

/**
 * Las sugerencias de la barra mientras se escribe. Espera un instante a que la persona deje de
 * teclear, y descarta la respuesta de lo que ya no es lo escrito: sin eso, la respuesta lenta de
 * «abo» pisaria a la de «abogado».
 */
export function useSugerencias(texto: string, cuantas = 5): Sugerencias {
  const q = texto.trim()
  const [estado, setEstado] = useState<{ q: string; pagina: PaginaDeTexto | null } | null>(null)

  useEffect(() => {
    if (q.length < MINIMO) return
    const clave = q.toLowerCase()
    const previo = guardado.get(clave)
    if (previo) {
      // Ya lo sabemos: se muestra al instante, sin esperar y sin llamar.
      const t = setTimeout(() => setEstado({ q, pagina: previo }), 0)
      return () => clearTimeout(t)
    }

    let vigente = true
    const t = setTimeout(() => {
      buscarTexto({ q })
        .then((pagina) => {
          if (guardado.size >= MAXIMO_GUARDADO) guardado.clear()
          guardado.set(clave, pagina)
          if (vigente) setEstado({ q, pagina })
        })
        .catch(() => { if (vigente) setEstado({ q, pagina: null }) })
    }, ESPERA_MS)
    return () => { vigente = false; clearTimeout(t) }
  }, [q])

  const listo = estado !== null && estado.q === q
  return {
    personas: listo && estado.pagina ? estado.pagina.resultados.slice(0, cuantas) : [],
    total: listo && estado.pagina ? estado.pagina.total : 0,
    cargando: q.length >= MINIMO && !listo,
    error: listo && estado.pagina === null,
  }
}
