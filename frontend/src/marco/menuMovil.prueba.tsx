import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { Contexto } from '../sesion/contexto'
import { Marco } from './Marco'

/*
 * El menu del telefono con el teclado: Escape lo cierra y el foco vuelve al boton que lo abrio.
 * En el telefono el menu es una hoja fija sobre la pagina (index.css), asi que al abrirlo el
 * foco entra en ella: si no, quien navega con teclado o lector seguiria en la barra de abajo
 * sin saber que se abrio algo encima.
 */
function pintar() {
  render(
    <MemoryRouter initialEntries={['/']}>
      <Contexto.Provider value={{ estado: 'fuera', usuario: null, recargar: async () => {}, salir: async () => {} }}>
        <Routes>
          <Route element={<Marco />}>
            <Route index element={<h1>Portada</h1>} />
          </Route>
        </Routes>
      </Contexto.Provider>
    </MemoryRouter>,
  )
  return {
    mas: within(screen.getByRole('navigation', { name: 'Accesos rápidos' })).getByRole('button', { name: 'Más' }),
    delaCabecera: screen.getByRole('button', { name: 'Menú' }),
  }
}

describe('el menu del telefono con el teclado', () => {
  it('al abrirlo, el foco entra al menu', async () => {
    const u = userEvent.setup()
    const { mas } = pintar()

    await u.click(mas)

    const menu = document.getElementById('menu-movil')
    expect(menu).not.toBeNull()
    expect(document.activeElement).toBe(within(menu!).getByRole('link', { name: 'Buscar' }))
  })

  it('Escape lo cierra y devuelve el foco a «Más», que lo abrio', async () => {
    const u = userEvent.setup()
    const { mas } = pintar()

    await u.click(mas)
    await u.keyboard('{Escape}')

    expect(document.getElementById('menu-movil')).toBeNull()
    expect(mas).toHaveAttribute('aria-expanded', 'false')
    expect(document.activeElement).toBe(mas)
  })

  it('si lo abrio el boton de la cabecera, el foco vuelve a ese boton y no a «Más»', async () => {
    const u = userEvent.setup()
    const { delaCabecera } = pintar()

    await u.click(delaCabecera)
    await u.keyboard('{Escape}')

    expect(document.getElementById('menu-movil')).toBeNull()
    expect(document.activeElement).toBe(delaCabecera)
  })

  it('solo Escape lo cierra: Tab recorre el menu sin cerrarlo', async () => {
    const u = userEvent.setup()
    const { mas } = pintar()

    await u.click(mas)
    await u.keyboard('{Tab}')

    const menu = document.getElementById('menu-movil')
    expect(menu).not.toBeNull()
    expect(document.activeElement).toBe(within(menu!).getByRole('link', { name: 'Comunas' }))
  })
})
