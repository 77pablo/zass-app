// @vitest-environment node
//
// La descripcion de una ficha se escribe dos veces: en la Function (servidor) y en src/marco/seo.ts
// (navegacion dentro de la app). Si se separan, lo que ve Google y lo que ve el navegador difieren.
import { describe, expect, it } from 'vitest'
import { datosDeFicha, descripcionDeFicha as deServidor, recortar as recortarServidor } from '../../functions/_comun/seo.js'
import { descripcionDeFicha as deNavegador, recortar as recortarNavegador } from '../marco/seo.ts'

const rubro = { id: 1, codigo: 'c', nombre: 'Corretaje de propiedades', icono: null }
const casos = [
  { nombrePublico: 'Marcela Soto', descripcion: 'Corredora.', rubro, zonas: [{ nombre: 'Centro', sector: 'c', esPrincipal: true }] },
  { nombrePublico: 'Ana', descripcion: null, rubro, zonas: [] },
  { nombrePublico: 'Ana', descripcion: '', rubro: null, zonas: [{ nombre: 'Labranza', sector: 'p', esPrincipal: false }] },
  { nombrePublico: 'Ana', descripcion: null, rubro: null, zonas: [] },
  { nombrePublico: ' Ana\n  Pérez ', descripcion: 'x '.repeat(400), rubro, zonas: [{ nombre: 'A', sector: 'c', esPrincipal: false }, { nombre: 'B', sector: 'c', esPrincipal: true }] },
  { nombrePublico: 'Ñandú', descripcion: 'Ñ'.repeat(500), rubro, zonas: [] },
]

describe('paridad servidor/navegador', () => {
  it.each(casos.map((c, i) => [i, c]))('misma descripcion en el caso %i', (_i, ficha) => {
    expect(deNavegador(ficha)).toBe(deServidor(datosDeFicha(ficha)))
  })

  it.each([['corto', 10], ['una frase con varias palabras para recortar bien', 20], ['x'.repeat(300), 50]])(
    'mismo recorte: %s', (texto, max) => {
      expect(recortarNavegador(texto, max)).toBe(recortarServidor(texto, max))
      expect(recortarNavegador(texto, max).length).toBeLessThanOrEqual(max)
    })
})
