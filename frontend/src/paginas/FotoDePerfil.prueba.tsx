import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import { Monograma } from '../marco/Monograma'
import { FotoDePerfil } from './FotoDePerfil'

let llamadas: { ruta: string; metodo?: string; archivo?: string; tipo?: string }[]
let respuesta: Response

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

const foto = (bytes: number, nombre = 'yo.jpg') =>
  new File([new Uint8Array(bytes)], nombre, { type: 'image/jpeg' })

beforeEach(() => {
  llamadas = []
  respuesta = json({ urlFoto: '/api/archivos/fotos/0123456789abcdef0123456789abcdef.jpg' })
  olvidarCsrf()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string, opciones?: RequestInit) => {
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    const cuerpo = opciones?.body
    llamadas.push({
      ruta, metodo: opciones?.method,
      archivo: cuerpo instanceof FormData ? (cuerpo.get('archivo') as File).name : undefined,
      tipo: (opciones?.headers as Record<string, string> | undefined)?.['Content-Type'],
    })
    return respuesta
  }))
})

describe('La foto del perfil (JQ-9)', () => {
  it('sin foto ofrece subir una, y no ofrece quitarla', () => {
    render(<FotoDePerfil tieneFoto={false} alCambiar={() => {}} />)

    expect(screen.getByLabelText(/Subir una foto/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Quitar foto' })).toBeNull()
  })

  it('subir manda el archivo como formulario a /api/mi/foto y avisa para recargar', async () => {
    const u = userEvent.setup()
    const alCambiar = vi.fn()
    render(<FotoDePerfil tieneFoto={false} alCambiar={alCambiar} />)

    await u.upload(screen.getByLabelText(/Subir una foto/), foto(4096))

    await vi.waitFor(() => expect(alCambiar).toHaveBeenCalledTimes(1))
    expect(llamadas).toEqual([{ ruta: '/api/mi/foto', metodo: 'POST', archivo: 'yo.jpg', tipo: undefined }])
  })

  it('una foto de más de 10 MB no sale del navegador', async () => {
    const u = userEvent.setup()
    render(<FotoDePerfil tieneFoto={false} alCambiar={() => {}} />)

    await u.upload(screen.getByLabelText(/Subir una foto/), foto(10 * 1024 * 1024 + 1))

    expect(await screen.findByText(/pesa más de 10 MB/)).toBeInTheDocument()
    expect(llamadas).toHaveLength(0)
  })

  it('con foto se puede cambiar o quitar, y quitar manda DELETE', async () => {
    const u = userEvent.setup()
    const alCambiar = vi.fn()
    respuesta = new Response(null, { status: 204 })
    render(<FotoDePerfil tieneFoto alCambiar={alCambiar} />)

    expect(screen.getByLabelText(/Cambiar foto/)).toBeInTheDocument()
    await u.click(screen.getByRole('button', { name: 'Quitar foto' }))

    await vi.waitFor(() => expect(alCambiar).toHaveBeenCalledTimes(1))
    expect(llamadas).toEqual([{ ruta: '/api/mi/foto', metodo: 'DELETE', archivo: undefined, tipo: 'application/json' }])
  })

  it('si el servidor rechaza la foto, se muestra su motivo', async () => {
    const u = userEvent.setup()
    respuesta = json({ title: 'Tipo de archivo no aceptado', detail: 'Usa una foto JPG, PNG o WebP.' }, 415)
    render(<FotoDePerfil tieneFoto={false} alCambiar={() => {}} />)

    await u.upload(screen.getByLabelText(/Subir una foto/), foto(100, 'no-es-foto.jpg'))

    expect(await screen.findByText('Usa una foto JPG, PNG o WebP.')).toBeInTheDocument()
  })
})

describe('Monograma', () => {
  it('con foto pinta la imagen, decorativa, y sin las iniciales', () => {
    const { container } = render(<Monograma nombre="Camila Painemal" foto="/api/archivos/fotos/x.jpg" />)

    const img = container.querySelector('img')
    expect(img).toHaveAttribute('src', '/api/archivos/fotos/x.jpg')
    expect(img).toHaveAttribute('alt', '')
    expect(container.textContent).not.toContain('CP')
  })

  it('con una foto nuestra ofrece los dos tamaños y deja elegir al navegador según dónde va', () => {
    const grande = '/api/archivos/fotos/0123456789abcdef0123456789abcdef.webp'
    const { container, rerender } = render(<Monograma nombre="Camila" tamano="m" foto={grande} />)

    const img = container.querySelector('img')!
    expect(img.getAttribute('srcset')).toBe(
      '/api/archivos/fotos/0123456789abcdef0123456789abcdef-96.webp 96w, ' + grande + ' 288w')
    expect(img.getAttribute('sizes')).toBe('44px')
    expect(img).toHaveAttribute('width', '44')
    expect(img).toHaveAttribute('height', '44')
    expect(img).toHaveAttribute('loading', 'lazy')

    // En la ficha, el grande: 96 px en pantalla y se pide al tiro.
    rerender(<Monograma nombre="Camila" tamano="g" foto={grande} />)
    const enFicha = container.querySelector('img')!
    expect(enFicha.getAttribute('sizes')).toBe('96px')
    expect(enFicha).toHaveAttribute('loading', 'eager')
  })

  it('una ruta que no armó el servidor no recibe un tamaño inventado', () => {
    const { container } = render(<Monograma nombre="Camila" foto="/api/archivos/fotos/x.jpg" />)

    expect(container.querySelector('img')!.hasAttribute('srcset')).toBe(false)
  })

  it('sin foto muestra las iniciales', () => {
    const { container } = render(<Monograma nombre="Camila Painemal" foto={null} />)

    expect(container.querySelector('img')).toBeNull()
    expect(container.textContent).toBe('CP')
  })
})
