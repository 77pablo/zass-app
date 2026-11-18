import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Contexto, type Usuario } from '../sesion/contexto'
import { Ficha } from './Ficha'

/**
 * La ficha publica, mirada como la mira alguien que esta decidiendo a quien llamar:
 * en que orden le llegan las cosas, si puede comprobar lo que se le promete, y si los
 * atajos que le ofrecemos llevan a alguna parte.
 */

const USUARIO: Usuario = {
  id: 'u1', correo: 'ana@zass.local', nombre: 'Ana', correoVerificado: true, roles: ['buscador'],
}

const FICHA = {
  id: 'p1', slug: 'marcela-soto', nombrePublico: 'Marcela Soto',
  descripcion: 'Quince años en el centro.', urlFoto: null,
  rubro: { id: 1, codigo: 'corretaje', nombre: 'Corretaje de propiedades', icono: null },
  zonas: [{ nombre: 'Centro', sector: 'centro', esPrincipal: true }],
  servicios: [{
    id: 'svc-1', nombre: 'Tasación comercial', duracionMinutos: 90,
    precioReferencia: 60000, modoAgenda: 'Automatico',
  }],
  credenciales: [
    {
      tipo: 'Titulo', nombre: 'Abogado', entidad: 'Universidad de La Frontera',
      obtenidaEn: '2010-03-01', etiqueta: 'Verificada', verificada: true,
    },
    {
      tipo: 'Curso', nombre: 'Curso de tasación', entidad: null,
      obtenidaEn: '2021-05-10', etiqueta: 'Declarada por el usuario', verificada: false,
    },
  ],
  promedioCalificacion: 4.6, totalResenas: 7,
  contacto: { telefono: null, whatsapp: '+56 9 5550 0101', correo: null, sitioWeb: null },
  publicadoEn: null,
  suscripcionVigente: true,
}

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), {
    status: estado, headers: { 'content-type': 'application/json' },
  })
}

function pintar(ficha: unknown = FICHA) {
  vi.stubGlobal('fetch', vi.fn(async (ruta: string) => {
    if (ruta.startsWith('/api/prestadores/marcela-soto/bloques')) {
      return json({
        prestadorServicioId: 'svc-1', perfilPrestadorId: 'p1', servicio: 'Tasación comercial',
        modoAgenda: 'Automatico', duracionMinutos: 90, anticipacionMinimaHoras: 2, bloques: [],
      })
    }
    if (ruta === '/api/prestadores/marcela-soto') return json(ficha)
    return json({ title: 'No autorizado' }, 401)
  }))

  return render(
    <MemoryRouter initialEntries={['/prestadores/marcela-soto']}>
      <Contexto.Provider
        value={{ estado: 'dentro', usuario: USUARIO, recargar: async () => {}, salir: async () => {} }}
      >
        <Routes><Route path="prestadores/:slug" element={<Ficha />} /></Routes>
      </Contexto.Provider>
    </MemoryRouter>,
  )
}

afterEach(() => vi.unstubAllGlobals())

describe('el orden en que la ficha cuenta las cosas', () => {
  it('las credenciales se leen ANTES de que se pida reservar una hora', async () => {
    pintar()
    await screen.findByRole('heading', { level: 1, name: 'Marcela Soto' })

    const titulos = screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent)

    // Candado con literales: la secuencia entera, no solo «credenciales antes que agendar».
    // Si alguien vuelve a bajar las credenciales, o cuela un bloque entre medio, esto cae.
    // El contacto va justo despues de quien es (decision de Pablo, 17-nov-2026).
    expect(titulos).toEqual([
      'Hablar con Marcela',
      'Sobre Marcela',
      'Qué ofrece',
      'Credenciales',
      'Agendar una hora con Marcela',
      'Dónde atiende',
      'Lo que dicen de Marcela',
    ])
  })

  it('el contacto va entre la persona y su presentacion, en el DOM y no solo en pantalla', async () => {
    pintar()
    const h1 = await screen.findByRole('heading', { level: 1, name: 'Marcela Soto' })
    const contacto = screen.getByRole('complementary', { name: 'Contacto' })
    const sobre = screen.getByRole('heading', { level: 2, name: 'Sobre Marcela' })

    // Lo que lee un lector de pantalla y lo que recorre el tabulador es lo mismo que se ve en un
    // telefono: quien es, como hablarle, y despues el detalle.
    expect(h1.compareDocumentPosition(contacto) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(contacto.compareDocumentPosition(sobre) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })
})

describe('las etiquetas de credencial se explican', () => {
  it('dice quien verifica y que significa cada etiqueta', async () => {
    pintar()
    await screen.findByRole('heading', { level: 1, name: 'Marcela Soto' })

    const nota = screen.getByText(/quiere decir que en ZASS! revisamos el documento/)
    // Visible, no solo presente: una nota escondida (`hidden`) pasaba igual con toBeInTheDocument.
    expect(nota).toBeVisible()
    expect(nota.textContent).toMatch(/«Verificada»/)
    expect(nota.textContent).toMatch(/«Declarada por el usuario».{0,80}todavía no la hemos revisado/)
  })

  it('sin credenciales no hay nada que explicar, y la explicacion no sale sola', async () => {
    pintar({ ...FICHA, credenciales: [] })
    await screen.findByRole('heading', { level: 1, name: 'Marcela Soto' })

    expect(screen.queryByRole('heading', { name: 'Credenciales' })).not.toBeInTheDocument()
    expect(screen.queryByText(/revisamos el documento/)).not.toBeInTheDocument()
  })
})

describe('el promedio lleva a las reseñas', () => {
  it('el promedio deja de ser un numero que hay que creer: enlaza a lo que dicen', async () => {
    pintar()
    await screen.findByRole('heading', { level: 1, name: 'Marcela Soto' })

    const enlace = screen.getByRole('link', { name: 'Leer las 7 reseñas' })
    expect(enlace).toHaveAttribute('href', '/prestadores/marcela-soto/resenas')
  })

  it('con una sola reseña el enlace no dice «1 reseñas»', async () => {
    pintar({ ...FICHA, promedioCalificacion: null, totalResenas: 1 })
    await screen.findByRole('heading', { level: 1, name: 'Marcela Soto' })

    expect(screen.getByRole('link', { name: 'Leer la reseña' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /reseñas/ })).not.toBeInTheDocument()
  })

  it('sin reseñas no se ofrece leer un vacio', async () => {
    pintar({ ...FICHA, promedioCalificacion: null, totalResenas: 0 })
    await screen.findByRole('heading', { level: 1, name: 'Marcela Soto' })

    expect(screen.getByText('Todavía no tiene reseñas.')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Leer la/ })).not.toBeInTheDocument()
  })
})

describe('la caja de contacto cuando no hay como contactar', () => {
  const SIN_CONTACTO = {
    ...FICHA,
    contacto: { telefono: null, whatsapp: null, correo: null, sitioWeb: null },
  }

  it('ofrece la salida que si existe: pedir una hora', async () => {
    pintar(SIN_CONTACTO)
    await screen.findByRole('heading', { level: 1, name: 'Marcela Soto' })

    const caja = screen.getByRole('complementary', { name: 'Contacto' })
    expect(caja.textContent).toMatch(/todavía no publicó cómo contactarla/)
    expect(caja.textContent).toMatch(/pedirle una hora desde su calendario/)
  })

  it('sin servicios no manda al calendario: ahi no hay nada que reservar', async () => {
    pintar({ ...SIN_CONTACTO, servicios: [] })
    await screen.findByRole('heading', { level: 1, name: 'Marcela Soto' })

    expect(screen.getByText(/todavía no publicó cómo contactarla/)).toBeInTheDocument()
    expect(screen.queryByText(/pedirle una hora desde su calendario/)).not.toBeInTheDocument()
  })
})

// La regla de CSS que sube el contacto en movil se vigila en Ficha.estilos.prueba.js: leer
// index.css necesita Node, y en un .tsx eso rompe `tsc -b` (sin tipos de Node en el frontend).
