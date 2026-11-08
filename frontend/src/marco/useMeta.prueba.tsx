import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { useMeta, type Meta } from './useMeta'

function Pagina(props: Meta) {
  useMeta(props)
  return null
}

const meta = (selector: string) => document.head.querySelector<HTMLMetaElement>(selector)

beforeEach(() => {
  document.head.innerHTML = `
    <meta name="description" content="por defecto" />
    <meta property="og:description" content="og por defecto" />`
})
afterEach(() => { document.head.innerHTML = '' })

describe('useMeta', () => {
  it('pone la descripcion, tambien la de Open Graph y la de Twitter', () => {
    render(<Pagina descripcion="Una descripción" />)
    expect(meta('meta[name="description"]')!.content).toBe('Una descripción')
    expect(meta('meta[property="og:description"]')!.content).toBe('Una descripción')
    expect(meta('meta[name="twitter:description"]')!.content).toBe('Una descripción')
  })

  it('crea las etiquetas que no existen y no duplica las que si', () => {
    render(<Pagina descripcion="x" />)
    expect(document.head.querySelectorAll('meta[name="description"]')).toHaveLength(1)
    expect(document.head.querySelectorAll('meta[name="twitter:description"]')).toHaveLength(1)
  })

  it('al salir de la pagina deja lo que habia: lo existente vuelve, lo creado se va', () => {
    const { unmount } = render(<Pagina descripcion="de la pagina" noindex />)
    expect(meta('meta[name="robots"]')).not.toBeNull()
    unmount()
    expect(meta('meta[name="description"]')!.content).toBe('por defecto')
    expect(meta('meta[property="og:description"]')!.content).toBe('og por defecto')
    expect(meta('meta[name="twitter:description"]')).toBeNull()
    expect(meta('meta[name="robots"]')).toBeNull()
  })

  it('noindex agrega robots noindex, nofollow', () => {
    render(<Pagina noindex />)
    expect(meta('meta[name="robots"]')!.content).toBe('noindex, nofollow')
    // Sin descripcion no toca la que habia.
    expect(meta('meta[name="description"]')!.content).toBe('por defecto')
  })

  it('sin noindex no agrega robots ni quita el que ya puso el servidor', () => {
    render(<Pagina descripcion="x" />)
    expect(meta('meta[name="robots"]')).toBeNull()

    document.head.insertAdjacentHTML('beforeend', '<meta name="robots" content="noindex, nofollow" />')
    render(<Pagina descripcion="y" />)
    expect(meta('meta[name="robots"]')!.content).toBe('noindex, nofollow')
  })

  it('al cambiar la descripcion se actualiza, y quitar noindex lo retira', () => {
    const { rerender } = render(<Pagina descripcion="uno" noindex />)
    rerender(<Pagina descripcion="dos" />)
    expect(meta('meta[name="description"]')!.content).toBe('dos')
    expect(meta('meta[name="robots"]')).toBeNull()
  })

  it('el texto va como contenido, no como HTML', () => {
    render(<Pagina descripcion={'"><script>alert(1)</script>'} />)
    expect(document.head.querySelector('script')).toBeNull()
    expect(meta('meta[name="description"]')!.content).toBe('"><script>alert(1)</script>')
  })
})
