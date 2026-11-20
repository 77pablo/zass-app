import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { CodigoQr } from './CodigoQr'
import { matrizDe } from './matrizQr'

/**
 * Sin doble de la librería: se usa la de verdad, porque lo que importa es que el `<svg>`
 * salga de lo que ella calcula para ESE texto.
 */
describe('CodigoQr', () => {
  it('genera una matriz cuadrada de un tamaño válido de QR', async () => {
    const m = await matrizDe('https://zass.cl/t/7KQ2-9XRM?m=qr')
    // Versiones 1 a 40: 21 a 177 módulos, de 4 en 4.
    expect(m.length).toBeGreaterThanOrEqual(21)
    expect((m.length - 21) % 4).toBe(0)
    expect(m.every((fila) => fila.length === m.length)).toBe(true)
    // El patrón de búsqueda de la esquina: 7 módulos oscuros arriba a la izquierda.
    expect(m[0].slice(0, 7)).toEqual(Array(7).fill(true))
  })

  it('textos distintos dan códigos distintos', async () => {
    const conQr = await matrizDe('https://zass.cl/t/7KQ2-9XRM?m=qr')
    const sinQr = await matrizDe('https://zass.cl/t/7KQ2-9XRM')
    expect(JSON.stringify(conQr)).not.toBe(JSON.stringify(sinQr))
  })

  it('dibuja un svg accesible con un módulo por cada casilla oscura', async () => {
    const texto = 'https://zass.cl/t/7KQ2-9XRM?m=qr'
    render(<CodigoQr texto={texto} alt="Código QR de la tarjeta 7KQ2-9XRM" />)

    const svg = await screen.findByRole('img', { name: 'Código QR de la tarjeta 7KQ2-9XRM' })
    const m = await matrizDe(texto)
    const oscuros = m.flat().filter(Boolean).length
    // +1 por el rectángulo blanco de fondo.
    expect(svg.querySelectorAll('rect')).toHaveLength(oscuros + 1)
  })
})
