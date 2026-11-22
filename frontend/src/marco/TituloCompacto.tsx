import { useEffect, useState } from 'react'
import { useLocation } from 'react-router-dom'

/** Alto de la barra fina (sin la muesca). Lo usa el margen del observador: el titulo grande se
 * da por «ido» cuando pasa por DEBAJO de la barra, no cuando sale de la pantalla. */
const ALTO_BARRA = 44

/**
 * El TITULO GRANDE de iOS que, al bajar, se vuelve una barra de vidrio fina con el titulo al
 * centro (Large Title → inline title). Solo en el telefono (CSS: en escritorio no se dibuja).
 *
 * No toca ninguna pagina: busca el <h1> que haya dentro de <main> y lo observa. Si la pagina lo
 * cambia (la ficha pone el nombre al cargar), un MutationObserver lo vuelve a enganchar.
 * La barra repite el <h1>, asi que es aria-hidden: el lector de pantalla ya lo oyo una vez.
 */
export function TituloCompacto() {
  const { pathname } = useLocation()
  const [titulo, setTitulo] = useState('')
  const [visible, setVisible] = useState(false)

  useEffect(() => {
    const main = document.getElementById('contenido')
    if (main === null || typeof IntersectionObserver === 'undefined') return
    let h1: HTMLElement | null = null
    let vigia: IntersectionObserver | null = null

    const enganchar = () => {
      const nuevo = main.querySelector('h1')
      setTitulo(nuevo?.textContent?.trim() ?? '')
      if (nuevo === h1) return
      vigia?.disconnect()
      h1 = nuevo
      setVisible(false)
      if (h1 === null) return
      vigia = new IntersectionObserver(
        ([e]) => setVisible(!e.isIntersecting && e.boundingClientRect.top < ALTO_BARRA),
        { rootMargin: `-${ALTO_BARRA}px 0px 0px 0px` },
      )
      vigia.observe(h1)
    }

    enganchar()
    const cambios = new MutationObserver(enganchar)
    cambios.observe(main, { childList: true, subtree: true, characterData: true })
    return () => { cambios.disconnect(); vigia?.disconnect() }
  }, [pathname])

  return (
    <div className="titulo-compacto" data-visible={visible && titulo !== ''} aria-hidden="true">
      <span className="titulo-compacto-texto">{titulo}</span>
    </div>
  )
}
