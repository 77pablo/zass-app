import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import { Contexto, type EstadoDeSesion, type Usuario } from '../sesion/contexto'
import { ResenasDelPrestador } from './ResenasDelPrestador'

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

const PERFIL = 'perfil-de-ana'
const OTRO_PERFIL = 'perfil-de-otro'

function ficha(extra: Record<string, unknown> = {}) {
  return {
    id: PERFIL, slug: 'ana-gasfiter', nombrePublico: 'Ana Pérez', descripcion: null, urlFoto: null,
    rubro: null, zonas: [], servicios: [], credenciales: [], promedioCalificacion: 4.5,
    totalResenas: 4, contacto: { telefono: null, whatsapp: null, correo: null, sitioWeb: null },
    publicadoEn: null, ...extra,
  }
}

function publica(extra: Record<string, unknown> = {}) {
  return {
    id: 'r1', calificacion: 5, comentario: 'Llegó puntual y dejó todo funcionando.',
    aspectos: ['puntualidad'], tipo: 'Servicio', editada: false,
    publicadaEn: '2026-11-10T14:00:00Z', respuesta: null, ...extra,
  }
}

let fichaActual: Record<string, unknown>
let paginas: Record<string, { resenas: unknown[]; siguienteCursor: string | null }>
let miPerfilId: string
let llamadas: { ruta: string; metodo: string; cuerpo?: Record<string, unknown> }[]

beforeEach(() => {
  fichaActual = ficha()
  paginas = { '': { resenas: [publica()], siguienteCursor: null } }
  miPerfilId = OTRO_PERFIL
  llamadas = []
  olvidarCsrf()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string, opciones?: RequestInit) => {
    const metodo = opciones?.method ?? 'GET'
    const cuerpo = opciones?.body ? JSON.parse(String(opciones.body)) as Record<string, unknown> : undefined
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    llamadas.push({ ruta, metodo, cuerpo })
    if (ruta.includes('/resenas')) {
      if (ruta.startsWith('/api/prestadores/')) {
        const cursor = new URL(ruta, 'http://x').searchParams.get('cursor') ?? ''
        return json(paginas[cursor])
      }
      return json({}, 204)   // reportar o responder: 204 / sin cuerpo util
    }
    if (ruta.startsWith('/api/prestadores/')) return json(fichaActual)
    if (ruta === '/api/mi/perfil-prestador') return json({ id: miPerfilId })
    throw new Error(`ruta sin doble: ${ruta}`)
  }))
})

function pintar(sesion: EstadoDeSesion = 'fuera', roles = ['buscador']) {
  const usuario: Usuario | null = sesion === 'dentro'
    ? { id: 'u', correo: 'a@b.cl', nombre: 'Luis', correoVerificado: true, roles } : null
  return render(
    <MemoryRouter initialEntries={['/prestadores/ana-gasfiter/resenas']}>
      <Contexto.Provider value={{ estado: sesion, usuario, recargar: async () => {}, salir: async () => {} }}>
        <Routes>
          <Route path="/prestadores/:slug/resenas" element={<ResenasDelPrestador />} />
        </Routes>
      </Contexto.Provider>
    </MemoryRouter>,
  )
}

describe('lo que dicen de alguien (S5)', () => {
  it('leer no pide cuenta: sin sesion se ven las reseñas', async () => {
    pintar('fuera')

    expect(await screen.findByText('Llegó puntual y dejó todo funcionando.')).toBeInTheDocument()
    expect(screen.getByLabelText('5 de 5')).toBeInTheDocument()
  })

  it('muestra la respuesta del prestador bajo la reseña (CU-17)', async () => {
    paginas[''] = {
      resenas: [publica({
        respuesta: { texto: 'Gracias, fue un gusto.', publicadaEn: '2026-11-11T10:00:00Z', editadaEn: null },
      })],
      siguienteCursor: null,
    }
    pintar()

    expect(await screen.findByText('Gracias, fue un gusto.')).toBeInTheDocument()
  })

  // RN-S5-11: con menos de tres reseñas se ve CUANTAS hay, pero no un promedio que aparentaria
  // una solidez que no tiene. El servidor manda el promedio nulo y la pantalla no lo inventa.
  it('con menos de 3 reseñas enseña el recuento y NO el promedio', async () => {
    fichaActual = ficha({ promedioCalificacion: null, totalResenas: 2 })
    pintar()

    expect(await screen.findByText(/2 reseñas\. El promedio se muestra desde la tercera/)).toBeInTheDocument()
    expect(screen.queryByText('4,5')).not.toBeInTheDocument()
  })

  it('con 3 o mas enseña el promedio', async () => {
    pintar()

    expect(await screen.findByText('4,5')).toBeInTheDocument()
    expect(screen.getByText(/de 5, según 4 reseñas/)).toBeInTheDocument()
  })

  // RN-S5-04 y CU-16 §7: el texto se guarda crudo y se ESCAPA al renderizar. La prueba no mira
  // que "se vea bien": mira que el navegador no haya creado el elemento que iba dentro.
  it('un comentario con HTML se muestra como texto, no se ejecuta', async () => {
    const veneno = '<img src=x onerror="alert(1)"> ojo con esto'
    paginas[''] = { resenas: [publica({ comentario: veneno })], siguienteCursor: null }
    const { container } = pintar()

    expect(await screen.findByText(veneno)).toBeInTheDocument()
    expect(container.querySelector('img')).toBeNull()
  })

  it('sin sesion no se puede reportar', async () => {
    pintar('fuera')

    await screen.findByText('Llegó puntual y dejó todo funcionando.')
    expect(screen.queryByRole('button', { name: /reportar/i })).not.toBeInTheDocument()
  })

  it('con sesion se reporta con un motivo tipificado (API-51)', async () => {
    pintar('dentro')
    const persona = userEvent.setup()

    await persona.click(await screen.findByRole('button', { name: /reportar/i }))
    await persona.click(screen.getByRole('radio', { name: /lenguaje de odio/i }))
    await persona.click(screen.getByRole('button', { name: /enviar el reporte/i }))

    await waitFor(() => {
      expect(llamadas.some((l) => l.ruta === '/api/resenas/r1/reporte')).toBe(true)
    })
    expect(llamadas.find((l) => l.ruta === '/api/resenas/r1/reporte')?.cuerpo)
      .toMatchObject({ motivo: 'LenguajeDeOdio' })
  })

  // Responder es del dueño del perfil reseñado y de nadie mas (RN-S5-06). Que el boton aparezca
  // a cualquiera invita a un 404 del servidor y hace creer que algo esta roto.
  it('responder solo aparece en el perfil propio', async () => {
    const { unmount } = pintar('dentro', ['buscador', 'prestador'])
    await screen.findByText('Llegó puntual y dejó todo funcionando.')
    expect(screen.queryByRole('button', { name: /^responder$/i })).not.toBeInTheDocument()
    unmount()

    miPerfilId = PERFIL
    pintar('dentro', ['buscador', 'prestador'])

    expect(await screen.findByRole('button', { name: /^responder$/i })).toBeInTheDocument()
  })

  it('el dueño publica su respuesta (API-50)', async () => {
    miPerfilId = PERFIL
    pintar('dentro', ['buscador', 'prestador'])
    const persona = userEvent.setup()

    await persona.click(await screen.findByRole('button', { name: /^responder$/i }))
    await persona.type(screen.getByRole('textbox', { name: /tu respuesta/i }), 'Gracias por avisar.')
    await persona.click(screen.getByRole('button', { name: /publicar mi respuesta/i }))

    await waitFor(() => {
      expect(llamadas.some((l) => l.ruta === '/api/resenas/r1/respuesta')).toBe(true)
    })
    expect(llamadas.find((l) => l.ruta === '/api/resenas/r1/respuesta')?.cuerpo)
      .toMatchObject({ texto: 'Gracias por avisar.' })
  })

  it('el dueño no se puede reseñar a si mismo: no hay enlace para dejar reseña', async () => {
    miPerfilId = PERFIL
    const { unmount } = pintar('dentro', ['buscador', 'prestador'])
    await screen.findByText('Llegó puntual y dejó todo funcionando.')
    expect(screen.queryByRole('link', { name: /dejar mi reseña/i })).not.toBeInTheDocument()
    unmount()

    miPerfilId = OTRO_PERFIL
    pintar('dentro', ['buscador', 'prestador'])

    expect(await screen.findByRole('link', { name: /dejar mi reseña/i }))
      .toHaveAttribute('href', '/prestadores/ana-gasfiter/resena')
  })

  it('«ver más» pide la pagina siguiente con el cursor y la suma a la lista', async () => {
    paginas[''] = { resenas: [publica()], siguienteCursor: 'CURSOR-2' }
    paginas['CURSOR-2'] = {
      resenas: [publica({ id: 'r2', comentario: 'La segunda vez también anduvo bien.' })],
      siguienteCursor: null,
    }
    pintar()
    const persona = userEvent.setup()

    await persona.click(await screen.findByRole('button', { name: /ver más reseñas/i }))

    expect(await screen.findByText('La segunda vez también anduvo bien.')).toBeInTheDocument()
    expect(screen.getByText('Llegó puntual y dejó todo funcionando.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /ver más reseñas/i })).not.toBeInTheDocument()
  })

  it('sin reseñas escritas lo dice, sin inventar nada', async () => {
    fichaActual = ficha({ promedioCalificacion: null, totalResenas: 0 })
    paginas[''] = { resenas: [], siguienteCursor: null }
    pintar()

    expect(await screen.findByText(/todavía no hay reseñas escritas/i)).toBeInTheDocument()
  })

  it('una reseña por inasistencia se rotula para que nadie la lea como del servicio', async () => {
    paginas[''] = { resenas: [publica({ tipo: 'Inasistencia' })], siguienteCursor: null }
    pintar()

    const tarjeta = (await screen.findByText('Llegó puntual y dejó todo funcionando.')).closest('article')
    expect(within(tarjeta as HTMLElement).getByText(/no habla del servicio/i)).toBeInTheDocument()
  })
})
