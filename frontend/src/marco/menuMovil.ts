import { createContext, useContext, useState } from 'react'
import { useLocation } from 'react-router-dom'

/**
 * El menu del telefono se abre desde dos lugares: el boton «Menu» de la cabecera y la pestaña
 * «Mas» de la barra inferior. Los dos tienen que ver y mover el MISMO estado.
 *
 * El menu guarda la RUTA en la que se abrio, y esta abierto solo mientras sigas en ella.
 * Asi se cierra al navegar sin un efecto que ponga estado: el efecto llegaria un
 * fotograma tarde y el menu se veria abierto sobre la pagina nueva.
 *
 * El proveedor (un componente) vive aparte, en ProveedorDeMenuMovil.tsx: un archivo que mezcla
 * componentes con hooks pierde la recarga en caliente de Vite.
 */
export interface MenuMovil {
  abierto: boolean
  alternar: () => void
  cerrar: () => void
}

export const ContextoDelMenuMovil = createContext<MenuMovil | null>(null)

export function useEstadoDelMenu(): MenuMovil {
  const { pathname } = useLocation()
  const [abiertoEn, setAbiertoEn] = useState<string | null>(null)
  const abierto = abiertoEn === pathname
  return { abierto, alternar: () => setAbiertoEn(abierto ? null : pathname), cerrar: () => setAbiertoEn(null) }
}

/**
 * Dentro del Marco, el estado compartido. Fuera de el (una prueba que pinta solo la cabecera),
 * un estado propio: la cabecera sigue funcionando sola, como antes.
 */
export function useMenuMovil(): MenuMovil {
  const compartido = useContext(ContextoDelMenuMovil)
  const propio = useEstadoDelMenu()
  return compartido ?? propio
}
