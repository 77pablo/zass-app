import { beforeEach, describe, expect, it, vi } from 'vitest'
import { listarRubros, listarZonas, serviciosDeRubro } from './catalogo'

const obtener = vi.fn()
vi.mock('./cliente', () => ({
  obtener: (...a: unknown[]) => obtener(...a),
}))

beforeEach(() => {
  obtener.mockReset()
  obtener.mockResolvedValue([])
})

// El catalogo (rubros, zonas, servicios) lo usan la busqueda Y el perfil del prestador,
// asi que vive aparte: ninguna de las dos funciones tiene que importar de la otra.
describe('catalogo', () => {
  it('pide los rubros a API-02', async () => {
    await listarRubros()
    expect(obtener).toHaveBeenLastCalledWith('/api/rubros')
  })

  it('pide las zonas a API-04', async () => {
    await listarZonas()
    expect(obtener).toHaveBeenLastCalledWith('/api/zonas')
  })

  it('pide los servicios de un rubro a API-03', async () => {
    await serviciosDeRubro(3)
    expect(obtener).toHaveBeenLastCalledWith('/api/rubros/3/servicios')
  })
})
