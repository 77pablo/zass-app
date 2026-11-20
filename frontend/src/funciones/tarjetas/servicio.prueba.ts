import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  desactivarTarjeta, enlaceDeTarjeta, enlaceQr, enlaceVisible, estadisticasDeTarjeta, misTarjetas,
} from './servicio'

const obtener = vi.fn()
const enviar = vi.fn()
vi.mock('../../api/cliente', () => ({
  obtener: (...a: unknown[]) => obtener(...a),
  enviar: (...a: unknown[]) => enviar(...a),
}))

beforeEach(() => {
  obtener.mockReset().mockResolvedValue([])
  enviar.mockReset().mockResolvedValue(undefined)
})

describe('servicio de tarjetas', () => {
  it('lista con GET /api/mi/tarjetas', async () => {
    await misTarjetas()
    expect(obtener).toHaveBeenCalledWith('/api/mi/tarjetas')
  })

  it('desactiva con POST (que lleva CSRF) y sin cuerpo', async () => {
    await desactivarTarjeta('abc-123')
    expect(enviar).toHaveBeenCalledTimes(1)
    const [ruta, cuerpo, metodo] = enviar.mock.calls[0]
    expect(ruta).toBe('/api/mi/tarjetas/abc-123/desactivar')
    expect(cuerpo).toBeUndefined()
    expect(metodo ?? 'POST').toBe('POST')
    expect(obtener).not.toHaveBeenCalled()
  })

  it('pide las estadísticas de esa tarjeta', async () => {
    await estadisticasDeTarjeta('abc-123')
    expect(obtener).toHaveBeenCalledWith('/api/mi/tarjetas/abc-123/estadisticas')
  })
})

describe('enlaces de la tarjeta', () => {
  it('apuntan siempre a zass.cl, no al origen de la página', () => {
    expect(enlaceDeTarjeta('7KQ2-9XRM')).toBe('https://zass.cl/t/7KQ2-9XRM')
    expect(enlaceVisible('7KQ2-9XRM')).toBe('zass.cl/t/7KQ2-9XRM')
  })

  it('el del QR lleva ?m=qr, que es lo que API-54 lee para contarlo como QR', () => {
    expect(enlaceQr('7KQ2-9XRM')).toBe('https://zass.cl/t/7KQ2-9XRM?m=qr')
  })
})
