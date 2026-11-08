// @vitest-environment node
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

describe('imagen para compartir', () => {
  const png = readFileSync(resolve(import.meta.dirname, '..', '..', 'public', 'og-zass.png'))

  it('es un PNG de 1200x630', () => {
    expect(png.subarray(1, 4).toString('latin1')).toBe('PNG')
    expect(png.readUInt32BE(16)).toBe(1200)
    expect(png.readUInt32BE(20)).toBe(630)
  })

  it('pesa menos de 600 KB (WhatsApp y otros descartan imagenes muy grandes)', () => {
    expect(png.length).toBeLessThan(600 * 1024)
  })
})
