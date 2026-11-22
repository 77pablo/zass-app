import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const estadoMfa = vi.fn()
vi.mock('../funciones/identidad/servicio', () => ({
  estadoMfa: () => estadoMfa(),
  cerrarSesion: vi.fn(),
}))
beforeEach(() => {
  estadoMfa.mockReset()
  estadoMfa.mockResolvedValue({ activo: true, activadoEn: null, codigosDeRespaldoRestantes: 10, obligatorio: true })
})
import { BarraInferior } from '../marco/BarraInferior'
import { Cabecera } from '../marco/Cabecera'
import { ProveedorDeMenuMovil } from '../marco/ProveedorDeMenuMovil'
import { Contexto, type EstadoDeSesion, type Usuario } from '../sesion/contexto'
import { NoEncontrada } from './NoEncontrada'
import { IndiceInterno, PuertaInterna, SoloSeccion } from './PanelInterno'

function sesion(estado: EstadoDeSesion, roles: string[]) {
  const usuario: Usuario | null = estado === 'dentro'
    ? { id: 'u', correo: 'a@b.cl', nombre: 'Ana', correoVerificado: true, roles } : null
  return { estado, usuario, recargar: async () => {}, salir: async () => {} }
}

function pintar(ruta: string, estado: EstadoDeSesion, roles: string[] = []) {
  return render(
    <MemoryRouter initialEntries={[ruta]}>
      <Contexto.Provider value={sesion(estado, roles)}>
        <Routes>
          <Route path="/interno" element={<PuertaInterna />}>
            <Route index element={<IndiceInterno />} />
            <Route path="verificacion" element={<SoloSeccion seccion="verificacion"><p>la mesa</p></SoloSeccion>} />
            <Route path="reportes" element={<SoloSeccion seccion="moderacion"><p>los reportes</p></SoloSeccion>} />
            <Route path="indicadores" element={<SoloSeccion seccion="administracion"><p>los indicadores</p></SoloSeccion>} />
            <Route path="tarjetas" element={<SoloSeccion seccion="administracion"><p>emitir</p></SoloSeccion>} />
            <Route path="auditoria" element={<SoloSeccion seccion="administracion"><p>la auditoria</p></SoloSeccion>} />
          </Route>
          <Route path="/entrar" element={<p>pantalla de entrar</p>} />
          <Route path="*" element={<NoEncontrada />} />
        </Routes>
      </Contexto.Provider>
    </MemoryRouter>,
  )
}

const robots = () => document.head.querySelector<HTMLMetaElement>('meta[name="robots"]')

afterEach(() => { robots()?.remove() })

describe('JQ-11: la puerta de /interno', () => {
  it.each([
    ['un buscador', ['buscador']],
    ['un prestador', ['buscador', 'prestador']],
  ])('%s ve una página que no existe, igual que una ruta inventada', (_, roles) => {
    const { container } = pintar('/interno', 'dentro', roles)
    const sinRol = container.innerHTML
    expect(screen.getByRole('heading', { level: 1, name: 'Esta página no existe.' })).toBeInTheDocument()
    expect(screen.queryByText(/Panel interno/)).toBeNull()
    expect(screen.queryByText(/prohibid|permiso/i)).toBeNull()

    // La misma página, letra por letra, que una dirección que de verdad no existe.
    const { container: inventada } = pintar('/no-existe-nada', 'dentro', roles)
    expect(sinRol).toBe(inventada.innerHTML)
  })

  it('sin sesión tampoco: no manda a /entrar, porque eso diría que detrás hay algo', () => {
    pintar('/interno/verificacion', 'fuera')
    expect(screen.getByRole('heading', { level: 1, name: 'Esta página no existe.' })).toBeInTheDocument()
    expect(screen.queryByText('pantalla de entrar')).toBeNull()
    expect(screen.queryByText('la mesa')).toBeNull()
  })

  it('mientras carga la sesión no nombra al panel', () => {
    pintar('/interno', 'cargando')
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Cargando…')
    expect(screen.queryByText(/Panel interno|Verificación/)).toBeNull()
  })

  it('el verificador entra y ve solo la verificación', () => {
    pintar('/interno', 'dentro', ['buscador', 'verificador'])
    expect(screen.getByRole('heading', { level: 1, name: 'Panel interno' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Verificación de credenciales/ }))
      .toHaveAttribute('href', '/interno/verificacion')
    expect(screen.queryByRole('link', { name: /Reseñas reportadas/ })).toBeNull()
  })

  it('el moderador entra y ve solo la moderación (JQ-12)', () => {
    pintar('/interno', 'dentro', ['buscador', 'moderador'])
    expect(screen.getByRole('link', { name: /Reseñas reportadas/ })).toHaveAttribute('href', '/interno/reportes')
    expect(screen.queryByRole('link', { name: /Verificación de credenciales/ })).toBeNull()
  })

  it('el administrador ve tarjetas y auditoría, pero sin verificador ni moderador no ve esas secciones: su API le respondería 403', () => {
    pintar('/interno', 'dentro', ['buscador', 'administrador'])
    expect(screen.getByRole('heading', { level: 1, name: 'Panel interno' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Emitir tarjetas/ })).toHaveAttribute('href', '/interno/tarjetas')
    expect(screen.getByRole('link', { name: /Auditoría/ })).toHaveAttribute('href', '/interno/auditoria')
    expect(screen.queryByRole('link', { name: /Verificación de credenciales|Reseñas reportadas/ })).toBeNull()
    expect(screen.getByRole('link', { name: /Indicadores/ })).toHaveAttribute('href', '/interno/indicadores')
  })

  it('el verificador y el moderador no ven los indicadores (JQ-14)', () => {
    pintar('/interno', 'dentro', ['verificador', 'moderador'])
    expect(screen.queryByRole('link', { name: /Indicadores/ })).toBeNull()
  })

  it('el verificador y el moderador no ven tarjetas ni auditoría en el índice (JQ-13)', () => {
    pintar('/interno', 'dentro', ['verificador', 'moderador'])
    expect(screen.queryByRole('link', { name: /Emitir tarjetas|Auditoría/ })).toBeNull()
  })

  it.each([
    ['el moderador, en la verificación', '/interno/verificacion', ['moderador'], 'la mesa'],
    ['el verificador, en los reportes', '/interno/reportes', ['verificador'], 'los reportes'],
    ['el administrador, en los reportes', '/interno/reportes', ['administrador'], 'los reportes'],
    ['un verificador sin administrador, en los indicadores', '/interno/indicadores', ['verificador'], 'los indicadores'],
    ['un verificador sin administrador, en emitir tarjetas', '/interno/tarjetas', ['verificador'], 'emitir'],
    ['un verificador sin administrador, en la auditoría', '/interno/auditoria', ['verificador'], 'la auditoria'],
    ['un moderador sin administrador, en emitir tarjetas', '/interno/tarjetas', ['moderador'], 'emitir'],
  ])('cada sección tiene su puerta: %s ve una página que no existe', (_, ruta, roles, contenido) => {
    pintar(ruta, 'dentro', roles)
    expect(screen.getByRole('heading', { level: 1, name: 'Esta página no existe.' })).toBeInTheDocument()
    expect(screen.queryByText(contenido)).toBeNull()
  })

  it('las subrutas también pasan por la puerta', () => {
    pintar('/interno/verificacion', 'dentro', ['buscador'])
    expect(screen.queryByText('la mesa')).toBeNull()
    pintar('/interno/verificacion', 'dentro', ['verificador'])
    expect(screen.getByText('la mesa')).toBeInTheDocument()
  })

  it('queda fuera del índice de buscadores', () => {
    pintar('/interno', 'dentro', ['verificador'])
    expect(robots()?.content).toBe('noindex, nofollow')
  })
})

describe('JQ-11: el enlace en «Más»', () => {
  function pintarMenu(roles: string[]) {
    return render(
      <MemoryRouter>
        <Contexto.Provider value={sesion('dentro', roles)}>
          <ProveedorDeMenuMovil>
            <Cabecera />
            <BarraInferior />
          </ProveedorDeMenuMovil>
        </Contexto.Provider>
      </MemoryRouter>,
    )
  }

  it.each([['verificador'], ['moderador'], ['administrador']])('con el rol %s aparece el enlace', async (rol) => {
    pintarMenu(['buscador', rol])
    await userEvent.click(screen.getByRole('button', { name: 'Más' }))
    expect(screen.getByRole('link', { name: /Panel interno/ })).toHaveAttribute('href', '/interno')
  })

  it('sin el rol no existe, ni con la hoja abierta', async () => {
    pintarMenu(['buscador', 'prestador'])
    await userEvent.click(screen.getByRole('button', { name: 'Más' }))
    expect(screen.getByRole('link', { name: /Mis tarjetas/ })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Panel interno/ })).toBeNull()
  })
})

describe('MFA obligatorio en el panel (22-nov-2026)', () => {
  it('un verificador sin MFA ve el aviso para activarlo en vez del panel', async () => {
    estadoMfa.mockResolvedValue({ activo: false, activadoEn: null, codigosDeRespaldoRestantes: 0, obligatorio: true })
    pintar('/interno/verificacion', 'dentro', ['buscador', 'verificador'])
    const enlace = await screen.findByRole('link', { name: 'Activar la verificación en dos pasos' })
    expect(enlace).toHaveAttribute('href', '/mi-cuenta/seguridad')
    expect(screen.queryByText('la mesa')).toBeNull()
  })

  it('con MFA activo el panel se abre', async () => {
    pintar('/interno/verificacion', 'dentro', ['buscador', 'verificador'])
    expect(await screen.findByText('la mesa')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Activar la verificación en dos pasos' })).toBeNull()
  })
})
