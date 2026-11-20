import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { BotonCompartir } from './BotonCompartir'

const URL_FICHA = 'https://zass.cl/prestadores/marcela-soto'

afterEach(() => vi.unstubAllGlobals())

function conNavegador(extra: Partial<Navigator>) {
  vi.stubGlobal('navigator', { ...navigator, ...extra })
}

describe('compartir una ficha', () => {
  it('en el telefono abre el menu del sistema con el titulo, el texto y el enlace', async () => {
    const share = vi.fn(async () => {})
    conNavegador({ share })
    render(<BotonCompartir titulo="Marcela Soto en ZASS!" texto="Marcela Soto · Corretaje" url={URL_FICHA} />)

    await userEvent.click(screen.getByRole('button', { name: 'Compartir' }))
    expect(share).toHaveBeenCalledWith({ title: 'Marcela Soto en ZASS!', text: 'Marcela Soto · Corretaje', url: URL_FICHA })
  })

  it('si cierra el menu sin elegir, no muestra ningun error', async () => {
    conNavegador({ share: vi.fn(async () => { throw new DOMException('cancelado', 'AbortError') }) })
    render(<BotonCompartir titulo="t" texto="x" url={URL_FICHA} />)

    await userEvent.click(screen.getByRole('button', { name: 'Compartir' }))
    expect(screen.getByRole('status')).toHaveTextContent('')
  })

  it('sin menu del sistema (escritorio), copia el enlace y lo dice', async () => {
    const writeText = vi.fn(async () => {})
    conNavegador({ share: undefined, clipboard: { writeText } as unknown as Clipboard })
    render(<BotonCompartir titulo="t" texto="x" url={URL_FICHA} />)

    await userEvent.click(screen.getByRole('button', { name: 'Compartir' }))
    expect(writeText).toHaveBeenCalledWith(URL_FICHA)
    expect(screen.getByRole('button', { name: 'Enlace copiado' })).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Enlace copiado')
  })

  it('si no puede ni compartir ni copiar, dice que hacer', async () => {
    conNavegador({ share: undefined, clipboard: { writeText: vi.fn(async () => { throw new Error('no') }) } as unknown as Clipboard })
    render(<BotonCompartir titulo="t" texto="x" url={URL_FICHA} />)

    await userEvent.click(screen.getByRole('button', { name: 'Compartir' }))
    expect(screen.getByRole('status')).toHaveTextContent('Copia la dirección de la página')
  })
})
