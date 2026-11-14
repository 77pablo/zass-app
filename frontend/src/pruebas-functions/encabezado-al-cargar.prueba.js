// @vitest-environment node
//
// Ninguna pantalla puede cargar a pagina completa sin encabezado.
//
// Esta prueba no mira UNA pantalla: lee TODAS y falla si alguna vuelve al patron viejo
// —devolver la pagina entera con solo una linea «Cargando…» y ningun `h1`—. Esta escrita
// asi, y no como una prueba por pantalla, por la misma razon que la de `robots.txt`: lo que
// se escapa es la pantalla NUEVA que alguien agrega sin acordarse, y una prueba por pantalla
// no cubre a la que todavia no existe.
//
// El defecto que la motiva se midio en un navegador de verdad el 14-nov-2026: al llegar a
// `/mi-ficha` la pagina no tenia NINGUN `h1` y el nombre aparecia tres segundos despues.
// Quien navega con lector llega a una pagina que no dice donde esta.
import { readFileSync, readdirSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const PAGINAS = resolve(import.meta.dirname, '..', 'paginas')

/** La forma vieja: la pagina entera es la linea de carga, sin titulo. */
const PATRON_VIEJO = /return <div className="contenedor ficha-pagina"><p className="cargando-linea"/

describe('la carga a pagina completa', () => {
  it('siempre lleva el titulo de la pantalla', () => {
    const culpables = readdirSync(PAGINAS)
      .filter((n) => n.endsWith('.tsx') && !n.endsWith('.prueba.tsx'))
      .filter((n) => PATRON_VIEJO.test(readFileSync(resolve(PAGINAS, n), 'utf8')))

    expect(culpables).toEqual([])
  })

  it('y hay pantallas que de verdad usan el componente', () => {
    // Si alguien borrara `PaginaCargando` y con el todas sus llamadas, la prueba de arriba
    // seguiria verde sobre la nada. Esta fija que el camino bueno existe.
    const usan = readdirSync(PAGINAS)
      .filter((n) => n.endsWith('.tsx') && !n.endsWith('.prueba.tsx'))
      .filter((n) => readFileSync(resolve(PAGINAS, n), 'utf8').includes('<PaginaCargando'))

    expect(usan.length).toBeGreaterThanOrEqual(6)
  })
})
