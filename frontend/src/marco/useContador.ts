import { useEffect, useState } from 'react'

function prefiereMenosMovimiento(): boolean {
  return typeof window.matchMedia === 'function'
    && window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

/**
 * Un numero que sube hasta su valor. Quien pidio menos movimiento lo ve ya puesto; y el lector de
 * pantalla nunca oye los valores intermedios porque quien lo usa pone el numero final en el texto
 * accesible.
 */
export function useContador(objetivo: number, duracionMs = 1000): number {
  const [valor, setValor] = useState(0)

  useEffect(() => {
    if (objetivo <= 0 || prefiereMenosMovimiento() || typeof requestAnimationFrame !== 'function') {
      const t = setTimeout(() => setValor(Math.max(0, objetivo)), 0)
      return () => clearTimeout(t)
    }
    let inicio: number | undefined
    let cuadro = 0
    const paso = (ahora: number) => {
      inicio ??= ahora
      const avance = Math.min(1, (ahora - inicio) / duracionMs)
      setValor(Math.round(objetivo * (1 - Math.pow(1 - avance, 3))))     // frena al llegar
      if (avance < 1) cuadro = requestAnimationFrame(paso)
    }
    cuadro = requestAnimationFrame(paso)
    return () => cancelAnimationFrame(cuadro)
  }, [objetivo, duracionMs])

  return valor
}
