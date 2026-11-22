import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import { Contexto, type Usuario } from '../sesion/contexto'
import { Ficha } from './Ficha'

/**
 * JQ-5: tocar WhatsApp, llamar o correo en la ficha deja constancia del contacto (API-46), que es
 * lo que arranca los 7 dias para poder reseñar. Antes la ficha no llamaba a nadie.
 *
 * Lo que se fija aqui: el canal correcto con origen «Ficha», una sola llamada por visita, el
 * sitio web fuera, y que un fallo del registro no le quite el enlace a nadie.
 */

const USUARIO: Usuario = {
  id: 'u1', correo: 'ana@zass.local', nombre: 'Ana', correoVerificado: true, roles: ['buscador'],
}

const FICHA = {
  id: 'p1', slug: 'marcela-soto', nombrePublico: 'Marcela Soto',
  descripcion: 'Quince años en el centro.', urlFoto: null,
  rubro: { id: 1, codigo: 'corretaje', nombre: 'Corretaje de propiedades', icono: null },
  zonas: [{ nombre: 'Centro', sector: 'centro', esPrincipal: true }],
  servicios: [],
  credenciales: [],
  promedioCalificacion: null, totalResenas: 0,
  contacto: {
    telefono: '+56 9 5550 0101', whatsapp: '+56 9 5550 0101',
    correo: 'marcela@correo.invalid', sitioWeb: 'https://ejemplo.invalid/marcela',
  },
  publicadoEn: null,
  suscripcionVigente: true,
}

interface Llamada { ruta: string; init?: RequestInit }
let llamadas: Llamada[]
let estadoDelContacto: number

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), {
    status: estado, headers: { 'content-type': 'application/json' },
  })
}

function pintar(ficha: typeof FICHA = FICHA) {
  vi.stubGlobal('fetch', vi.fn(async (ruta: string, init?: RequestInit) => {
    llamadas.push({ ruta, init })
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    if (ruta === '/api/contactos') {
      return estadoDelContacto === 200
        ? json({ id: 'c1' })
        : json({ title: 'No elegible', detail: 'No puedes.' }, estadoDelContacto)
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

const alContacto = () => llamadas.filter((l) => l.ruta === '/api/contactos')
const cuerpo = (l: Llamada) => JSON.parse(String(l.init?.body)) as Record<string, string>
/** Da tiempo a que una llamada que NO deberia salir, si sale, alcance a verse. */
const esperar = () => new Promise((r) => setTimeout(r, 50))

beforeEach(() => {
  olvidarCsrf()
  llamadas = []
  estadoDelContacto = 200
})
afterEach(() => vi.unstubAllGlobals())

describe('la ficha registra el contacto al tocar un canal (JQ-5, API-46)', () => {
  it.each([
    [/WhatsApp/, 'Whatsapp'],
    [/Llamar/, 'Telefono'],
    [/Escribir un correo/, 'Correo'],
  ])('tocar %s registra el canal %s, con origen Ficha y keepalive', async (nombre, canal) => {
    pintar()
    fireEvent.click(await screen.findByRole('link', { name: nombre }))

    await waitFor(() => expect(alContacto()).toHaveLength(1))
    const [llamada] = alContacto()
    expect(llamada.init?.method).toBe('POST')
    expect(cuerpo(llamada)).toEqual({ perfilId: 'p1', origen: 'Ficha', canal })
    expect(llamada.init?.keepalive).toBe(true)
  })

  it('el sitio web no registra contacto: no es hablar con nadie', async () => {
    pintar()
    fireEvent.click(await screen.findByRole('link', { name: /sitio web/ }))

    await esperar()
    expect(alContacto()).toHaveLength(0)
  })

  it('varios toques en la misma visita hacen una sola llamada, con el canal del primero', async () => {
    pintar()
    fireEvent.click(await screen.findByRole('link', { name: /WhatsApp/ }))
    fireEvent.click(screen.getByRole('link', { name: /Llamar/ }))
    fireEvent.click(screen.getByRole('link', { name: /WhatsApp/ }))

    await waitFor(() => expect(alContacto()).toHaveLength(1))
    await esperar()
    expect(alContacto()).toHaveLength(1)
    expect(cuerpo(alContacto()[0]).canal).toBe('Whatsapp')
  })

  it('si el registro falla, el enlace abre igual y no aparece ningún aviso', async () => {
    estadoDelContacto = 403
    pintar()
    const wasap = await screen.findByRole('link', { name: /WhatsApp/ })

    // fireEvent.click devuelve false si alguien llamo a preventDefault.
    expect(fireEvent.click(wasap)).toBe(true)

    await waitFor(() => expect(alContacto().length).toBeGreaterThan(0))
    await esperar()
    expect(wasap).toHaveAttribute('href', 'https://wa.me/56955500101')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})

describe('«Guardar contacto» baja la vCard (CS-19, API-07)', () => {
  it('apunta a la vCard de esta ficha y se descarga', async () => {
    pintar()
    const guardar = await screen.findByRole('link', { name: /Guardar contacto/ })

    expect(guardar).toHaveAttribute('href', '/api/prestadores/marcela-soto/vcard')
    expect(guardar).toHaveAttribute('download')
  })

  it('guardar el contacto no registra un contacto: no es hablar con nadie', async () => {
    pintar()
    fireEvent.click(await screen.findByRole('link', { name: /Guardar contacto/ }))

    await esperar()
    expect(alContacto()).toHaveLength(0)
  })

  it('con solo un sitio web no aparece: una vCard con el puro nombre no sirve', async () => {
    pintar({ ...FICHA, contacto: { telefono: null, whatsapp: null, correo: null, sitioWeb: 'https://ejemplo.invalid/marcela' } } as unknown as typeof FICHA)

    await screen.findByRole('link', { name: /sitio web/ })
    expect(screen.queryByRole('link', { name: /Guardar contacto/ })).not.toBeInTheDocument()
  })
})
