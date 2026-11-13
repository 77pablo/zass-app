import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { Contexto, type Usuario } from '../sesion/contexto'
import { Ficha } from './Ficha'

/**
 * RN-S6-10 en la pantalla: la ficha de quien dejo de pagar se ve ENTERA, pero sin contactar
 * ni agendar, y lo dice.
 *
 * El contacto ya viene vacio del servidor (ServicioBusqueda.FichaPublicaAsync): aqui se
 * comprueba lo otro, que es lo que solo se ve aqui — que la ficha no quede muda, que no se
 * pida el calendario, y que no aparezca la promesa de «contactar no te cuesta nada» cuando
 * justamente no se puede contactar.
 */

const USUARIO: Usuario = {
  id: 'u1', correo: 'ana@zass.local', nombre: 'Ana', correoVerificado: true, roles: ['buscador'],
}

const FICHA_AL_DIA = {
  id: 'p1', slug: 'marcela-soto', nombrePublico: 'Marcela Soto',
  descripcion: 'Quince años en el centro.', urlFoto: null,
  rubro: { id: 1, codigo: 'corretaje', nombre: 'Corretaje de propiedades', icono: null },
  zonas: [{ nombre: 'Centro', sector: 'centro', esPrincipal: true }],
  servicios: [{
    id: 'svc-1', nombre: 'Tasación comercial', duracionMinutos: 90,
    precioReferencia: 60000, modoAgenda: 'Automatico',
  }],
  credenciales: [],
  promedioCalificacion: null, totalResenas: 0,
  contacto: { telefono: null, whatsapp: '+56 9 5550 0101', correo: null, sitioWeb: null },
  publicadoEn: null,
  suscripcionVigente: true,
}

/** Lo que manda el servidor con la suscripcion caida: el contacto ya viene vacio. */
const FICHA_IMPAGA = {
  ...FICHA_AL_DIA,
  contacto: { telefono: null, whatsapp: null, correo: null, sitioWeb: null },
  suscripcionVigente: false,
}

let llamadas: string[]

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), {
    status: estado, headers: { 'content-type': 'application/json' },
  })
}

function pintar(ficha: unknown) {
  llamadas = []
  vi.stubGlobal('fetch', vi.fn(async (ruta: string) => {
    llamadas.push(ruta)
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

beforeEach(() => { llamadas = [] })
afterEach(() => vi.unstubAllGlobals())

describe('ficha de un prestador sin suscripción vigente (RN-S6-10)', () => {
  it('la ficha se sigue viendo entera: nombre, oficio, servicios y zonas', async () => {
    pintar(FICHA_IMPAGA)

    expect(await screen.findByRole('heading', { level: 1, name: 'Marcela Soto' })).toBeInTheDocument()
    expect(screen.getByText('Quince años en el centro.')).toBeInTheDocument()
    expect(screen.getByText(/Tasación comercial/)).toBeInTheDocument()
    expect(screen.getByText(/desde \$60\.000/)).toBeInTheDocument()
    expect(screen.getByText('Centro')).toBeInTheDocument()
  })

  it('no hay botón de contactar, y lo dice en vez de dejar la caja muda', async () => {
    pintar(FICHA_IMPAGA)

    await screen.findByRole('heading', { level: 1, name: 'Marcela Soto' })
    expect(screen.queryByRole('link', { name: /WhatsApp/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Llamar/ })).not.toBeInTheDocument()
    expect(screen.getByText(/no está recibiendo contactos por ahora/)).toBeInTheDocument()
    // La caja vacía de siempre diría algo falso: no es que no haya publicado el contacto.
    expect(screen.queryByText(/todavía no publicó cómo contactarla/)).not.toBeInTheDocument()
    // Y no se promete lo que no se puede cumplir.
    expect(screen.queryByText(/Contactar no te cuesta nada/)).not.toBeInTheDocument()
  })

  it('no se puede agendar, y el calendario ni siquiera se pide', async () => {
    pintar(FICHA_IMPAGA)

    await screen.findByRole('heading', { level: 1, name: 'Marcela Soto' })
    expect(screen.getByText(/no está tomando horas por ahora/)).toBeInTheDocument()
    expect(llamadas.some((r) => r.includes('/bloques'))).toBe(false)
  })

  it('ningún texto le cuenta al visitante que la persona dejó de pagar', async () => {
    pintar(FICHA_IMPAGA)

    await screen.findByRole('heading', { level: 1, name: 'Marcela Soto' })
    for (const palabra of [/pag(ó|o|a)/i, /suscripci/i, /vencid/i, /deud/i, /moros/i]) {
      expect(document.body.textContent ?? '').not.toMatch(palabra)
    }
  })

  it('con la suscripción al día nada de esto aparece: contacto y calendario como siempre', async () => {
    pintar(FICHA_AL_DIA)

    await screen.findByRole('heading', { level: 1, name: 'Marcela Soto' })
    expect(screen.getByRole('link', { name: /WhatsApp/ }))
      .toHaveAttribute('href', 'https://wa.me/56955500101')
    expect(screen.getByText(/Contactar no te cuesta nada/)).toBeInTheDocument()
    expect(screen.queryByText(/no está recibiendo contactos por ahora/)).not.toBeInTheDocument()
    expect(screen.queryByText(/no está tomando horas por ahora/)).not.toBeInTheDocument()
    await vi.waitFor(() => expect(llamadas.some((r) => r.includes('/bloques'))).toBe(true))
  })
})
