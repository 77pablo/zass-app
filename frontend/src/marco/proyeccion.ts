import type { Zona } from '../api/catalogo'

export const LADO = 520
export const CENTRO = LADO / 2
export const RADIO_MAXIMO = 215
export const SEPARACION = 40

export interface PuntoDeRadar {
  id: number
  nombre: string
  x: number
  y: number
  n: number
}

/**
 * Proyecta los barrios alrededor del centro de la ciudad: la DIRECCION es la real (Labranza queda al
 * poniente, Pichicautín al sur) y la distancia se comprime con una raiz, porque la ciudad tiene un nucleo
 * apretado y un par de barrios lejanos, y con escala lineal el nucleo saldria como un solo racimo. Despues
 * se separan los puntos que quedan encimados, para que cada barrio se pueda tocar.
 *
 * La longitud se corrige por el coseno de la latitud: a -38,7° un grado de longitud mide menos que uno
 * de latitud.
 */
export function proyectarBarrios(
  zonas: Zona[], conteo: (zonaId: number) => number,
): PuntoDeRadar[] {
  if (zonas.length === 0) return []
  const base = zonas.find((z) => z.nombre === 'Centro') ?? zonas[0]
  const k = Math.cos(base.centroLatitud * Math.PI / 180)

  const crudos = zonas.map((z) => {
    const dx = (z.centroLongitud - base.centroLongitud) * k
    const dy = base.centroLatitud - z.centroLatitud           // el norte queda arriba
    return { z, dx, dy, d: Math.hypot(dx, dy) }
  })
  const dMax = Math.max(...crudos.map((c) => c.d), 1e-9)

  const puntos: PuntoDeRadar[] = crudos.map(({ z, dx, dy, d }) => {
    const r = d === 0 ? 0 : RADIO_MAXIMO * Math.pow(d / dMax, 0.5)
    const a = Math.atan2(dy, dx)
    return { id: z.id, nombre: z.nombre, x: CENTRO + r * Math.cos(a), y: CENTRO + r * Math.sin(a), n: conteo(z.id) }
  })

  // Separacion: se empujan de a pares hasta que ninguno pisa al otro. Es determinista (mismo orden, mismas vueltas).
  for (let vuelta = 0; vuelta < 60; vuelta++) {
    let movido = false
    for (let i = 0; i < puntos.length; i++) {
      for (let j = i + 1; j < puntos.length; j++) {
        const a = puntos[i], b = puntos[j]
        let dx = b.x - a.x, dy = b.y - a.y
        let dist = Math.hypot(dx, dy)
        if (dist >= SEPARACION) continue
        if (dist < 0.01) { dx = 1; dy = 0; dist = 1 }
        const empuje = (SEPARACION - dist) / 2
        const ux = dx / dist, uy = dy / dist
        // El centro no se mueve: es la referencia del radar.
        const fijoA = a.nombre === 'Centro', fijoB = b.nombre === 'Centro'
        if (!fijoA) { a.x -= ux * empuje * (fijoB ? 2 : 1); a.y -= uy * empuje * (fijoB ? 2 : 1) }
        if (!fijoB) { b.x += ux * empuje * (fijoA ? 2 : 1); b.y += uy * empuje * (fijoA ? 2 : 1) }
        movido = true
      }
    }
    // Que ninguno se salga del circulo.
    for (const p of puntos) {
      const dx = p.x - CENTRO, dy = p.y - CENTRO
      const d = Math.hypot(dx, dy)
      if (d > RADIO_MAXIMO + 8) { p.x = CENTRO + (dx / d) * (RADIO_MAXIMO + 8); p.y = CENTRO + (dy / d) * (RADIO_MAXIMO + 8) }
    }
    if (!movido) break
  }
  return puntos
}

