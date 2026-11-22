import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  activarTarjeta, desactivarTarjeta, enlaceDeTarjeta, enlaceQr, enlaceVisible, estadisticasDeTarjeta,
  misTarjetas, normalizarCodigo, segmentoValido,
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

describe('activar (API-55, CU-19)', () => {
  it('va por /api/mi con enviar (token CSRF), con el segmento escapado y el código en el cuerpo', async () => {
    await activarTarjeta('7KQ2-9XRM', 'ABCD-EFGH-JKMN')
    await activarTarjeta('../x?y', 'ABCD-EFGH-JKMN')

    expect(enviar).toHaveBeenNthCalledWith(1, '/api/mi/tarjetas/7KQ2-9XRM/activar', { codigo: 'ABCD-EFGH-JKMN' })
    expect(enviar).toHaveBeenNthCalledWith(2, '/api/mi/tarjetas/..%2Fx%3Fy/activar', { codigo: 'ABCD-EFGH-JKMN' })
  })

  it.each([
    ['abcd-efgh-jkmn', 'ABCD-EFGH-JKMN'],
    [' ABCD EFGH JKMN ', 'ABCD-EFGH-JKMN'],
    ['ABCDEFGHJKMN', 'ABCD-EFGH-JKMN'],
    ['23456789ABCD', '2345-6789-ABCD'],
  ])('normaliza el código %j como lo espera la API', (escrito, esperado) => {
    expect(normalizarCodigo(escrito)).toBe(esperado)
  })

  // 0, O, 1, I y L no existen en el alfabeto: un código con ellas no es de ninguna tarjeta.
  it.each(['', 'ABCD-EFGH', 'ABCD-EFGH-JKMNP', 'ABCD-EFGH-JKM0', 'ABCD-EFGH-JKMO', 'ABCD-EFGH-JKM1', 'ABCD-EFGH-JKMI', 'ABCD-EFGH-JKML', 'ABCD_EFGH_JKMN'])(
    'rechaza %j', (escrito) => {
      expect(normalizarCodigo(escrito)).toBeNull()
    })

  it('el segmento: en mayúsculas si tiene la forma impresa; si no, null', () => {
    expect(segmentoValido('7kq2-9xrm')).toBe('7KQ2-9XRM')
    expect(segmentoValido(undefined)).toBeNull()
    expect(segmentoValido('7KQ2-9XR')).toBeNull()
    expect(segmentoValido('7KQ29XRM')).toBeNull()
    expect(segmentoValido('7KQ2-9XR0')).toBeNull()
    expect(segmentoValido('7KQ2-9XRM/../x')).toBeNull()
  })
})
