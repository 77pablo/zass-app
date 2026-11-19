import { createContext, useContext, useEffect, useRef, useState } from 'react'
import { useLocation } from 'react-router-dom'

/**
 * El menu del telefono se abre desde dos lugares: el boton «Menu» de la cabecera y la pestaña
 * «Mas» de la barra inferior. Los dos tienen que ver y mover el MISMO estado.
 *
 * El menu guarda la RUTA en la que se abrio, y esta abierto solo mientras sigas en ella.
 * Asi se cierra al navegar sin un efecto que ponga estado: el efecto llegaria un
 * fotograma tarde y el menu se veria abierto sobre la pagina nueva.
 *
 * Con el teclado: al abrirse, el foco entra al menu (en el telefono es una hoja fija encima de
 * la pagina, y quien navega con teclado o lector seguiria abajo sin saber que se abrio). Escape
 * lo cierra y el foco vuelve al boton que lo abrio, sea el de la cabecera o «Mas».
 *
 * El proveedor (un componente) vive aparte, en ProveedorDeMenuMovil.tsx: un archivo que mezcla
 * componentes con hooks pierde la recarga en caliente de Vite.
 */
export interface MenuMovil {
  abierto: boolean
  /**
   * Los botones lo pasan tal cual a `onClick`: del evento sale QUE boton abrio el menu, para
   * devolverle el foco. No se usa `document.activeElement` porque Safari no enfoca un boton al
   * tocarlo.
   */
  alternar: (evento?: { currentTarget: EventTarget | null }) => void
}

/** El id que la cabecera le pone al menu (y que los botones nombran en aria-controls). */
const ID_DEL_MENU = 'menu-movil'

export const ContextoDelMenuMovil = createContext<MenuMovil | null>(null)

export function useEstadoDelMenu(): MenuMovil {
  const { pathname } = useLocation()
  const [abiertoEn, setAbiertoEn] = useState<string | null>(null)
  const abridor = useRef<HTMLElement | null>(null)
  const abierto = abiertoEn === pathname

  useEffect(() => {
    if (!abierto) return
    document.getElementById(ID_DEL_MENU)?.querySelector<HTMLElement>('a[href], button')?.focus()

    const alTeclear = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return
      setAbiertoEn(null)
      abridor.current?.focus()
    }
    document.addEventListener('keydown', alTeclear)
    return () => document.removeEventListener('keydown', alTeclear)
  }, [abierto])

  return {
    abierto,
    alternar: (evento) => {
      if (!abierto) {
        const boton = evento?.currentTarget
        abridor.current = boton instanceof HTMLElement ? boton : null
      }
      setAbiertoEn(abierto ? null : pathname)
    },
  }
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
