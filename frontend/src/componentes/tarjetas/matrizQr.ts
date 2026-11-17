/** La matriz del código: `true` es un módulo oscuro. */
export type Matriz = boolean[][]

/**
 * Genera la matriz con `qrcode-generator`, que se carga AQUÍ y solo aquí: el `import()`
 * deja la librería en un trozo aparte que baja la primera vez que alguien abre un QR en
 * «Mis tarjetas», no con el resto del sitio.
 *
 * Nivel de corrección M: aguanta un 15 % de daño, de sobra para una pantalla.
 */
export async function matrizDe(texto: string): Promise<Matriz> {
  const { default: qrcode } = await import('qrcode-generator')
  const qr = qrcode(0, 'M')
  qr.addData(texto)
  qr.make()
  const n = qr.getModuleCount()
  return Array.from({ length: n }, (_, fila) =>
    Array.from({ length: n }, (_, col) => qr.isDark(fila, col)))
}
