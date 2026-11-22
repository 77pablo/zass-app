import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import { Contexto, type Usuario } from '../sesion/contexto'

/*
 * Las rutas DE VERDAD de App.tsx. PanelInterno.prueba.tsx prueba la puerta con rutas armadas a
 * mano; esto prueba que cada ruta de /interno esté conectada a la sección que corresponde: una
 * ruta montada sin su `SoloSeccion` (o con la sección equivocada) se escaparía de la otra prueba.
 */

beforeEach(() => {
  vi.stubGlobal('fetch', vi.fn(async (ruta: string) => {
    const vacio = ruta.includes('/cola') ? [] : {}
    return new Response(JSON.stringify(vacio), { status: 200, headers: { 'content-type': 'application/json' } })
  }))
})

function pintar(ruta: string, roles: string[]) {
  const usuario: Usuario = { id: 'u', correo: 'a@b.cl', nombre: 'Ana', correoVerificado: true, roles }
  return render(
    <MemoryRouter initialEntries={[ruta]}>
      <Contexto.Provider value={{ estado: 'dentro', usuario, recargar: async () => {}, salir: async () => {} }}>
        <App />
      </Contexto.Provider>
    </MemoryRouter>,
  )
}

const NO_EXISTE = 'Esta página no existe.'

describe('JQ-11/12/13: cada ruta de /interno con el rol que pide su API', () => {
  it.each([
    ['/interno/verificacion', 'verificador', 'Verificación de credenciales'],
    ['/interno/reportes', 'moderador', 'Reseñas reportadas'],
    ['/interno/tarjetas', 'administrador', 'Emitir tarjetas'],
    ['/interno/auditoria', 'administrador', 'Auditoría'],
  ])('%s abre con el rol %s', async (ruta, rol, titulo) => {
    pintar(ruta, ['buscador', rol])
    // waitFor y no findBy: la pantalla de carga pone un h1 con el mismo título y luego lo reemplaza.
    await waitFor(() => expect(screen.getByRole('heading', { level: 1, name: titulo })).toBeInTheDocument())
    expect(screen.queryByRole('heading', { level: 1, name: NO_EXISTE })).toBeNull()
  })

  it.each([
    ['/interno/verificacion', ['administrador', 'moderador']],
    ['/interno/reportes', ['verificador', 'administrador']],
    ['/interno/tarjetas', ['verificador', 'moderador']],
    ['/interno/auditoria', ['verificador', 'moderador']],
    ['/interno/lo-que-sea', ['verificador', 'moderador', 'administrador']],
  ])('%s, sin su rol (con %j), es una página que no existe', async (ruta, roles) => {
    pintar(ruta, ['buscador', ...roles])
    expect(await screen.findByRole('heading', { level: 1, name: NO_EXISTE })).toBeInTheDocument()
  })
})
