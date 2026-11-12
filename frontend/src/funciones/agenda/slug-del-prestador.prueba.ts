import { beforeEach, describe, expect, it, vi } from 'vitest'
import { slugDelPrestador } from './slug-del-prestador'

const buscarTexto = vi.fn()
vi.mock('../busqueda/servicio', () => ({
  buscarTexto: (...a: unknown[]) => buscarTexto(...a),
}))

const LA_CITA = { perfilPrestadorId: 'perfil-otro', prestador: 'Ana Pérez' }

function resultados(...rs: { perfilId: string; slug: string }[]) {
  return { resultados: rs, total: rs.length, pagina: 1, hayMas: false }
}

beforeEach(() => {
  buscarTexto.mockReset().mockResolvedValue(resultados())
})

describe('el slug del prestador de una cita', () => {
  it('si la cita es mia como prestador, sale de mi propio perfil y no se sale a buscar', async () => {
    const slug = await slugDelPrestador(
      { perfilPrestadorId: 'perfil-mio', prestador: 'Yo' },
      { id: 'perfil-mio', slug: 'estudio-mio' },
    )

    expect(slug).toBe('estudio-mio')
    expect(buscarTexto).not.toHaveBeenCalled()
  })

  it('si la cita es de otro, se busca por el nombre y se toma el del MISMO identificador', async () => {
    buscarTexto.mockResolvedValue(resultados(
      { perfilId: 'perfil-homonimo', slug: 'ana-perez' },
      { perfilId: 'perfil-otro', slug: 'ana-perez-2' },
    ))

    const slug = await slugDelPrestador(LA_CITA, { id: 'perfil-mio', slug: 'estudio-mio' })

    expect(buscarTexto).toHaveBeenCalledWith({ q: 'Ana Pérez' })
    expect(slug).toBe('ana-perez-2')
  })

  /**
   * Es la guarda que importa: dos personas se pueden llamar igual, y abrir el calendario de la
   * equivocada ofreceria horas de alguien con quien no hay ninguna cita.
   */
  it('si ningun resultado tiene ese identificador, devuelve null en vez del primero que suene', async () => {
    buscarTexto.mockResolvedValue(resultados({ perfilId: 'perfil-homonimo', slug: 'ana-perez' }))

    expect(await slugDelPrestador(LA_CITA, null)).toBeNull()
  })

  it('sin perfil propio tambien resuelve el de otro', async () => {
    buscarTexto.mockResolvedValue(resultados({ perfilId: 'perfil-otro', slug: 'ana-perez' }))

    expect(await slugDelPrestador(LA_CITA, null)).toBe('ana-perez')
  })
})
