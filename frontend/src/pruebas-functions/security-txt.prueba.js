// @vitest-environment node
//
// /.well-known/security.txt (RFC 9116): a quien encuentre un fallo de seguridad le dice a quien
// avisar. Esta prueba lee el archivo y la regla de public/_headers; no mide zass.cl en vivo.
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const RAIZ = resolve(import.meta.dirname, '..', '..')
const TEXTO = readFileSync(resolve(RAIZ, 'public', '.well-known', 'security.txt'), 'utf8')
const HEADERS = readFileSync(resolve(RAIZ, 'public', '_headers'), 'utf8')

const campos = (nombre) =>
  TEXTO.split(/\r?\n/).filter((l) => l.startsWith(`${nombre}: `)).map((l) => l.slice(nombre.length + 2))

describe('security.txt', () => {
  it('trae un Contact con mailto o https (obligatorio)', () => {
    const contactos = campos('Contact')
    expect(contactos.length).toBeGreaterThan(0)
    for (const c of contactos) expect(c).toMatch(/^(mailto:|https:\/\/)/)
  })

  it('trae UN Expires en ISO 8601, en el futuro y a menos de un ano (obligatorio)', () => {
    const vence = campos('Expires')
    expect(vence).toHaveLength(1)
    expect(vence[0]).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z$/)
    const fecha = Date.parse(vence[0])
    // Si esta prueba cae porque paso la fecha, hay que renovar el archivo: un security.txt vencido
    // se debe tratar como si no existiera (RFC 9116, 2.5.5).
    expect(fecha).toBeGreaterThan(Date.now())
    expect(fecha - Date.now()).toBeLessThanOrEqual(366 * 24 * 3600 * 1000)
  })

  it('declara idiomas y su direccion canonica', () => {
    expect(campos('Preferred-Languages')).toEqual(['es, en'])
    expect(campos('Canonical')).toEqual(['https://zass.cl/.well-known/security.txt'])
  })

  it('se sirve como texto plano, no como la pagina de la aplicacion', () => {
    const lineas = HEADERS.split(/\r?\n/)
    const inicio = lineas.findIndex((l) => l.trim() === '/.well-known/security.txt')
    expect(inicio).toBeGreaterThanOrEqual(0)
    expect(lineas[inicio + 1].trim()).toBe('Content-Type: text/plain; charset=utf-8')
  })
})
