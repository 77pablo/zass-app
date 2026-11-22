import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../../api/cliente'
import { agrupar, guardarGrupo, type PreferenciaTipo } from './preferencias'

/* ES-3b · API-43. Como se agrupan los tipos y que se manda al cambiar un grupo. */

const p = (tipo: string, grupo: string, correo = true, push = true, correoFijo = false): PreferenciaTipo =>
  ({ tipo, grupo, correo, push, correoFijo })

describe('agrupar', () => {
  it('respeta el orden del servidor y junta los tipos de cada grupo', () => {
    const g = agrupar([p('A', 'citas'), p('B', 'recordatorios'), p('C', 'citas')])
    expect(g.map((x) => x.clave)).toEqual(['citas', 'recordatorios'])
    expect(g[0].tipos).toEqual(['A', 'C'])
  })

  it('un canal esta encendido solo si lo esta en todos los tipos del grupo', () => {
    const [g] = agrupar([p('A', 'citas', true, true), p('B', 'citas', false, true), p('C', 'citas', true, false)])
    expect(g.correo).toBe(false)
    expect(g.push).toBe(false)
    const [h] = agrupar([p('A', 'citas'), p('B', 'citas')])
    expect(h.correo && h.push).toBe(true)
  })

  it('el correo del grupo es fijo si lo es en cualquiera de sus tipos', () => {
    const [g] = agrupar([p('A', 'cobros', true, true, true), p('B', 'cobros')])
    expect(g.correoFijo).toBe(true)
    const [h] = agrupar([p('A', 'citas'), p('B', 'citas')])
    expect(h.correoFijo).toBe(false)
  })
})

describe('guardarGrupo', () => {
  let cuerpos: unknown[]
  beforeEach(() => {
    cuerpos = []
    olvidarCsrf()
    vi.stubGlobal('fetch', vi.fn(async (entrada: RequestInfo | URL, opciones?: RequestInit) => {
      if (String(entrada).includes('/api/auth/csrf')) return new Response(JSON.stringify({ token: 't' }), { status: 200, headers: { 'content-type': 'application/json' } })
      cuerpos.push({ metodo: opciones?.method, ruta: String(entrada), cuerpo: JSON.parse(String(opciones?.body)) })
      return new Response(JSON.stringify({ preferencias: [] }), { status: 200, headers: { 'content-type': 'application/json' } })
    }))
  })

  it('manda un PUT con todos los tipos del grupo', async () => {
    const [g] = agrupar([p('A', 'citas'), p('B', 'citas')])
    await guardarGrupo(g, false, true)
    expect(cuerpos).toEqual([{ metodo: 'PUT', ruta: expect.stringContaining('/api/mi/notificaciones/preferencias'),
      cuerpo: { preferencias: [{ tipo: 'A', correo: false, push: true }, { tipo: 'B', correo: false, push: true }] } }])
  })

  it('con el correo fijo nunca manda el correo apagado', async () => {
    const [g] = agrupar([p('A', 'cobros', true, true, true)])
    await guardarGrupo(g, false, false)
    expect((cuerpos[0] as { cuerpo: unknown }).cuerpo).toEqual({ preferencias: [{ tipo: 'A', correo: true, push: false }] })
  })
})
