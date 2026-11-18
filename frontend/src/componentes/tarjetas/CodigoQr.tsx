import { useEffect, useState } from 'react'
import { matrizDe, type Matriz } from './matrizQr'

/** Margen en módulos alrededor del código: la norma pide 4 para que los lectores lo encuentren. */
const MARGEN = 4

/**
 * Un QR dibujado con React como `<svg>`: sin `innerHTML` ni data URL, así que no le pide nada
 * a la CSP. Blanco y negro fijos a propósito, también en tema oscuro: un QR invertido lo
 * leen mal muchos teléfonos.
 */
export function CodigoQr({ texto, alt }: { texto: string; alt: string }) {
  const [matriz, setMatriz] = useState<Matriz | null>(null)

  useEffect(() => {
    let vigente = true
    void matrizDe(texto).then((m) => { if (vigente) setMatriz(m) })
    return () => { vigente = false }
  }, [texto])

  if (matriz === null) return null

  const lado = matriz.length + MARGEN * 2
  return (
    <svg
      className="codigo-qr" role="img" aria-label={alt}
      viewBox={`0 0 ${lado} ${lado}`} width="200" height="200" shapeRendering="crispEdges"
    >
      <rect width={lado} height={lado} fill="#fff" />
      {matriz.flatMap((fila, y) => fila.map((oscuro, x) => (oscuro
        ? <rect key={`${x}-${y}`} x={x + MARGEN} y={y + MARGEN} width="1" height="1" fill="#000" />
        : null)))}
    </svg>
  )
}
