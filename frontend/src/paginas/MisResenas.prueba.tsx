import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import { Contexto, type EstadoDeSesion, type Usuario } from '../sesion/contexto'
import { MisResenas } from './MisResenas'

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

/**
 * Un instante MUY posterior a cualquier ejecucion de esta prueba, para que "dentro del plazo"
 * no dependa del dia en que se corra. En junio Chile esta en UTC-4, asi que las 18:30 UTC son
 * las 14:30 de Chile: la prueba comprueba ademas que la hora se traduce y no se pinta en UTC.
 */
const PLAZO_VIVO = '2099-06-15T18:30:00Z'
const PLAZO_VENCIDO = '2020-01-01T00:00:00Z'

function mia(extra: Record<string, unknown> = {}) {
  return {
    id: 'r1', perfilNombre: 'Estudio Espinoza', perfilSlug: 'estudio-espinoza',
    calificacion: 5, comentario: 'Llegó a la hora y dejó todo funcionando como corresponde.',
    aspectos: ['puntualidad'], tipo: 'Servicio', origen: 'Cita', estado: 'Publicada',
    editada: false, publicadaEn: '2026-11-12T13:00:00Z', editableHasta: PLAZO_VIVO, ...extra,
  }
}

let mias: unknown[]
let fallaLaLista: boolean
let llamadas: { ruta: string; metodo: string; cuerpo?: unknown }[]
let alEditar: () => Response

beforeEach(() => {
  mias = []
  fallaLaLista = false
  llamadas = []
  alEditar = () => json(mia())
  olvidarCsrf()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string, opciones?: RequestInit) => {
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    if (ruta === '/api/mi/resenas') {
      return fallaLaLista ? json({ title: 'caida', detail: 'Se cayó todo.' }, 500) : json(mias)
    }
    llamadas.push({
      ruta, metodo: opciones?.method ?? 'GET',
      cuerpo: opciones?.body ? JSON.parse(String(opciones.body)) : undefined,
    })
    return alEditar()
  }))
})

function pintar(sesion: EstadoDeSesion = 'dentro') {
  const usuario: Usuario | null = sesion === 'dentro'
    ? { id: 'u', correo: 'a@b.cl', nombre: 'Ana', correoVerificado: true, roles: ['buscador'] } : null
  return render(
    <MemoryRouter initialEntries={['/mis-resenas']}>
      <Contexto.Provider value={{ estado: sesion, usuario, recargar: async () => {}, salir: async () => {} }}>
        <Routes>
          <Route path="/mis-resenas" element={<MisResenas />} />
          <Route path="/entrar" element={<p>pantalla de entrar</p>} />
        </Routes>
      </Contexto.Provider>
    </MemoryRouter>,
  )
}

describe('mis reseñas', () => {
  it('sin sesion manda a entrar', async () => {
    pintar('fuera')

    expect(await screen.findByText('pantalla de entrar')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Mis reseñas' })).not.toBeInTheDocument()
  })

  it('sin reseñas lo dice e invita a buscar', async () => {
    pintar()

    expect(await screen.findByText(/Todavía no has dejado ninguna reseña/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Buscar a alguien' })).toHaveAttribute('href', '/buscar')
  })

  it('si la lista no carga, lo dice y deja reintentar', async () => {
    fallaLaLista = true
    pintar()

    expect(await screen.findByRole('alert')).toHaveTextContent('Se cayó todo.')
    fallaLaLista = false
    mias = [mia()]
    await userEvent.click(screen.getByRole('button', { name: 'Reintentar' }))

    expect(await screen.findByRole('heading', { name: 'Estudio Espinoza' })).toBeInTheDocument()
  })

  /** El orden es CONTRATO del servidor (API-65): la pantalla no vuelve a ordenar por su cuenta. */
  it('respeta el orden en que llegan, sin reordenar', async () => {
    mias = [
      mia({ id: 'a', perfilNombre: 'La más nueva', perfilSlug: 'nueva', publicadaEn: '2026-11-12T13:00:00Z' }),
      mia({ id: 'b', perfilNombre: 'La del medio', perfilSlug: 'medio', publicadaEn: '2026-10-01T13:00:00Z' }),
      mia({ id: 'c', perfilNombre: 'La más vieja', perfilSlug: 'vieja', publicadaEn: '2026-01-05T13:00:00Z' }),
    ]
    pintar()

    await screen.findByRole('heading', { name: 'La más nueva' })
    const titulos = screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent)
    expect(titulos).toEqual(['La más nueva', 'La del medio', 'La más vieja'])
  })

  it('cada reseña enlaza a la ficha de quien reseñó', async () => {
    mias = [mia()]
    pintar()

    expect(await screen.findByRole('link', { name: 'Estudio Espinoza' }))
      .toHaveAttribute('href', '/prestadores/estudio-espinoza')
  })
})

describe('la ventana de 72 horas', () => {
  it('dentro del plazo dice hasta cuando, en hora de Chile, y deja editar', async () => {
    mias = [mia({ editableHasta: PLAZO_VIVO })]
    pintar()

    expect(await screen.findByText(/Puedes editarla hasta el/)).toHaveTextContent('15 de junio a las 14:30 h')
    expect(screen.getByRole('button', { name: 'Editar mi reseña' })).toBeInTheDocument()
  })

  /** 4D.5: pasado el plazo NO se ofrece un boton que el servidor va a rechazar. */
  it('vencido el plazo lo dice y no ofrece el boton', async () => {
    mias = [mia({ editableHasta: PLAZO_VENCIDO })]
    pintar()

    expect(await screen.findByText('El plazo para editarla terminó')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Editar mi reseña' })).not.toBeInTheDocument()
  })

  it('editar manda el PUT y deja la fila actualizada sin perder a quien reseñó', async () => {
    mias = [mia({ comentario: 'Lo dije corto y me quedé con ganas de contarlo mejor.' })]
    alEditar = () => json({
      id: 'r1', perfilPrestadorId: 'p1', calificacion: 4,
      comentario: 'Ahora sí lo cuento entero: llegó puntual y explicó cada cosa que hizo.',
      aspectos: ['puntualidad'], tipo: 'Servicio', estado: 'Publicada', editada: true,
      publicadaEn: '2026-11-12T13:00:00Z', editableHasta: PLAZO_VIVO,
    })
    pintar()

    await userEvent.click(await screen.findByRole('button', { name: 'Editar mi reseña' }))
    const texto = screen.getByRole('textbox')
    await userEvent.clear(texto)
    await userEvent.type(texto, 'Ahora sí lo cuento entero: llegó puntual y explicó cada cosa que hizo.')
    await userEvent.click(screen.getByRole('button', { name: 'Guardar los cambios' }))

    await waitFor(() => expect(llamadas).toHaveLength(1))
    expect(llamadas[0].ruta).toBe('/api/resenas/r1')
    expect(llamadas[0].metodo).toBe('PUT')
    expect(await screen.findByText(/Ahora sí lo cuento entero/)).toBeInTheDocument()
    // El nombre y el slug no vienen en la respuesta de API-49: se conservan, no se pierden.
    expect(screen.getByRole('link', { name: 'Estudio Espinoza' }))
      .toHaveAttribute('href', '/prestadores/estudio-espinoza')
    expect(screen.getByText(/· editada/)).toBeInTheDocument()
  })

  it('cancelar la edicion deja el texto original intacto', async () => {
    mias = [mia({ comentario: 'El texto que ya estaba escrito y que no quiero perder.' })]
    pintar()

    await userEvent.click(await screen.findByRole('button', { name: 'Editar mi reseña' }))
    await userEvent.type(screen.getByRole('textbox'), ' basura')
    await userEvent.click(screen.getByRole('button', { name: 'Volver' }))

    expect(screen.getByText('El texto que ya estaba escrito y que no quiero perder.')).toBeInTheDocument()
    expect(llamadas).toHaveLength(0)
  })
})

describe('las que ya no estan publicadas', () => {
  it('una retirada se muestra con su estado y no se puede editar aunque este a tiempo', async () => {
    mias = [mia({ estado: 'Retirada', editableHasta: PLAZO_VIVO })]
    pintar()

    expect(await screen.findByText('Retirada tras una revisión')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Editar mi reseña' })).not.toBeInTheDocument()
    // Sigue siendo suya: el comentario no desaparece de la lista.
    expect(screen.getByText(/dejó todo funcionando/)).toBeInTheDocument()
  })

  it('una oculta en moderacion dice que la estan revisando', async () => {
    mias = [mia({ estado: 'OcultaEnModeracion', editableHasta: PLAZO_VIVO })]
    pintar()

    expect(await screen.findByText('Oculta mientras un moderador la revisa')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Editar mi reseña' })).not.toBeInTheDocument()
  })
})

describe('de donde salio la reseña', () => {
  it('la de contacto directo lleva el rotulo', async () => {
    mias = [mia({ origen: 'Directo' })]
    pintar()

    expect(await screen.findByText('Contacto directo, sin cita')).toBeInTheDocument()
  })

  /** La de cita NO lleva etiqueta: un rotulo en todas las tarjetas deja de decir nada. */
  it('la de cita no lleva ninguna etiqueta de origen', async () => {
    mias = [mia({ origen: 'Cita' })]
    pintar()

    await screen.findByRole('heading', { name: 'Estudio Espinoza' })
    expect(screen.queryByText('Contacto directo, sin cita')).not.toBeInTheDocument()
    expect(screen.queryByText(/con cita/i)).not.toBeInTheDocument()
  })
})
