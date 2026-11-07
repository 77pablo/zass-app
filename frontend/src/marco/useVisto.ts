import { useEffect, useState, type RefObject } from 'react'

function sinAnimaciones(): boolean {
  return typeof IntersectionObserver === 'undefined'
    || (typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches)
}

/**
 * Si el elemento ya entro en pantalla (y se queda en «visto»: las animaciones de entrada pasan una vez).
 * Sin soporte o con «reducir movimiento» devuelve verdadero desde el principio: lo que se anima para entrar
 * nunca puede quedar escondido porque algo fallo.
 */
export function useVisto(ref: RefObject<Element | null>): boolean {
  const [visto, setVisto] = useState(sinAnimaciones)

  useEffect(() => {
    if (visto) return
    const el = ref.current
    if (!el) return
    const observador = new IntersectionObserver(
      (entradas) => {
        if (entradas.some((e) => e.isIntersecting)) {
          setVisto(true)
          observador.disconnect()
        }
      },
      { threshold: 0.12, rootMargin: '0px 0px -6% 0px' },
    )
    observador.observe(el)
    return () => observador.disconnect()
  }, [ref, visto])

  return visto
}
