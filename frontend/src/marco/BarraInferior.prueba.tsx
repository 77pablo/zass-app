import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { Contexto, type EstadoDeSesion, type Usuario } from '../sesion/contexto'
import { Marco } from './Marco'

const ANA: Usuario = { id: 'u1', correo: 'ana@zass.local', nombre: 'Ana', correoVerificado: true, roles: ['buscador'] }

function pintar(ruta = '/', estado: EstadoDeSesion = 'fuera') {
  render(
    <MemoryRouter initialEntries={[ruta]}>
      <Contexto.Provider value={{ estado, usuario: estado === 'dentro' ? ANA : null, recargar: async () => {}, salir: async () => {} }}>
        <Routes>
          <Route element={<Marco />}>
            <Route index element={<h1>Portada</h1>} />
            <Route path="buscar" element={<h1>Buscar</h1>} />
            <Route path="comunas" element={<h1>Comunas</h1>} />
          </Route>
        </Routes>
      </Contexto.Provider>
    </MemoryRouter>,
  )
  return within(screen.getByRole('navigation', { name: 'Accesos rápidos' }))
}

describe('la barra inferior del telefono', () => {
  it('fuera de la sesion: Inicio, Buscar, Entrar y Más', () => {
    const barra = pintar()

    expect(barra.getByRole('link', { name: 'Inicio' })).toHaveAttribute('href', '/')
    expect(barra.getByRole('link', { name: 'Buscar' })).toHaveAttribute('href', '/buscar')
    expect(barra.getByRole('link', { name: 'Entrar' })).toHaveAttribute('href', '/entrar')
    expect(barra.getByRole('button', { name: 'Más' })).toBeInTheDocument()
    expect(barra.queryByRole('link', { name: 'Mis citas' })).not.toBeInTheDocument()
  })

  it('dentro: Mis citas en vez de Entrar', () => {
    const barra = pintar('/', 'dentro')

    expect(barra.getByRole('link', { name: 'Mis citas' })).toHaveAttribute('href', '/mis-citas')
    expect(barra.queryByRole('link', { name: 'Entrar' })).not.toBeInTheDocument()
  })

  it('mientras la sesion carga no muestra ni Entrar ni Mis citas: no salta al llegar', () => {
    const barra = pintar('/', 'cargando')

    expect(barra.queryByRole('link', { name: 'Entrar' })).not.toBeInTheDocument()
    expect(barra.queryByRole('link', { name: 'Mis citas' })).not.toBeInTheDocument()
  })

  it('marca donde estas, e «Inicio» solo en la portada', () => {
    const barra = pintar('/buscar')

    expect(barra.getByRole('link', { name: 'Buscar' })).toHaveAttribute('aria-current', 'page')
    expect(barra.getByRole('link', { name: 'Inicio' })).not.toHaveAttribute('aria-current')
  })

  it('«Más» abre el MISMO menu que el boton de la cabecera, y se cierra al elegir', async () => {
    const u = userEvent.setup()
    const barra = pintar()
    const mas = barra.getByRole('button', { name: 'Más' })
    const delaCabecera = screen.getByRole('button', { name: 'Menú' })

    await u.click(mas)
    expect(mas).toHaveAttribute('aria-expanded', 'true')
    // Un solo estado: la cabecera tambien lo ve abierto.
    expect(delaCabecera).toHaveAttribute('aria-expanded', 'true')
    const menu = document.getElementById('menu-movil')
    expect(menu).not.toBeNull()

    await u.click(within(menu!).getByRole('link', { name: 'Comunas' }))
    expect(await screen.findByRole('heading', { name: 'Comunas' })).toBeInTheDocument()
    expect(document.getElementById('menu-movil')).toBeNull()
    expect(mas).toHaveAttribute('aria-expanded', 'false')
  })
})
