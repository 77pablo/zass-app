import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import { Contexto, type EstadoDeSesion, type Usuario } from '../sesion/contexto'
import { Planes } from './Planes'

/*
 * La página de planes. Lo que se vigila aquí es sobre todo que NO prometa lo que no puede
 * dar: que sin cupos no ofrezca el botón, que sin ficha mande a publicarla primero, y que
 * dos clics seguidos no tomen dos cupos.
 */

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), {
    status: estado, headers: { 'content-type': 'application/json' },
  })
}

const PLANES = [
  {
    id: 2, codigo: 'mensual', nombre: 'ZASS! Mensual', descripcion: 'El plan de siempre.',
    precioMensual: 7990, diasPrueba: 0, maxServicios: 10, maxTarjetas: 1,
    permiteCredenciales: true,
  },
  {
    id: 3, codigo: 'fundador', nombre: 'ZASS! Fundador', descripcion: 'Precio de lanzamiento.',
    precioMensual: 5990, diasPrueba: 0, maxServicios: 10, maxTarjetas: 1,
    permiteCredenciales: true,
  },
]

let cupos: number
let fallaElCatalogo: boolean
let llamadas: string[]
let claves: (string | null)[]
let respuestaDelCupo: () => Response
let respuestaDeCotizar: () => Response

beforeEach(() => {
  cupos = 37
  fallaElCatalogo = false
  llamadas = []
  claves = []
  respuestaDelCupo = () => json({
    id: 'i1', codigoPlan: 'fundador', precioMensual: 0, descuento: 0, total: 0, cupon: null,
    estado: 'Pendiente', urlParaPagar: '/pago/inscripcion?intento=i1',
    referenciaPasarela: 'ref_1', nueva: true,
  })
  respuestaDeCotizar = () => json({
    codigoPlan: 'mensual', nombrePlan: 'ZASS! Mensual', precioMensual: 7990,
    descuento: 2397, total: 5593, cupon: 'LANZAMIENTO30', porcentajeDescuento: 30,
    esElPrimerMesQueSePaga: true, detalle: 'con tu código',
  })

  olvidarCsrf()
  // Una clave DISTINTA en cada llamada, a proposito: si la pantalla la generara por clic en
  // vez de al montarse, la prueba de CC-04 lo tiene que notar. Un doble clavado a un valor
  // fijo haria pasar esa prueba sin vigilar nada.
  let contador = 0
  vi.stubGlobal('crypto', {
    ...globalThis.crypto,
    randomUUID: () => `clave-de-prueba-${(contador += 1)}`,
  })
  vi.stubGlobal('fetch', vi.fn(async (entrada: RequestInfo | URL, opciones?: RequestInit) => {
    const ruta = String(entrada)
    llamadas.push(ruta)
    if (ruta.includes('/api/mi/suscripcion/fundador')) {
      claves.push(new Headers(opciones?.headers).get('Idempotency-Key'))
    }
    if (ruta.includes('/api/auth/csrf')) return json({ token: 't' })
    if (ruta.includes('/api/planes')) {
      return fallaElCatalogo
        ? json({ title: 'Error', detail: 'La API no responde.' }, 500)
        : json({ planes: PLANES, cuposFundadorDisponibles: cupos })
    }
    if (ruta.includes('/api/mi/suscripcion/fundador')) return respuestaDelCupo()
    if (ruta.includes('/api/mi/cobros/cotizar')) return respuestaDeCotizar()
    throw new Error('ruta no esperada: ' + ruta)
  }))
})

function sesion(estado: EstadoDeSesion, roles: string[] = []) {
  const usuario: Usuario | null = estado === 'dentro'
    ? {
        id: 'u1', correo: 'pablo@zass.cl', nombre: 'Pablo', correoVerificado: true, roles,
      }
    : null
  return { estado, usuario, recargar: async () => {}, salir: async () => {} }
}

function pintar(valor = sesion('fuera')) {
  return render(
    <MemoryRouter>
      <Contexto.Provider value={valor}>
        <Planes />
      </Contexto.Provider>
    </MemoryRouter>,
  )
}

describe('Planes', () => {
  it('muestra los dos planes con su precio y cuántos cupos quedan', async () => {
    pintar()

    expect(await screen.findByText('ZASS! Fundador')).toBeTruthy()
    expect(screen.getByText('ZASS! Mensual')).toBeTruthy()
    expect(screen.getByText('$5.990')).toBeTruthy()
    expect(screen.getByText('$7.990')).toBeTruthy()
    expect(screen.getByText('37')).toBeTruthy()
  })

  it('sin cupos el plan fundador aparece cerrado y SIN botón', async () => {
    cupos = 0
    pintar(sesion('dentro', ['prestador']))

    expect(await screen.findByText(/ya están tomados/i)).toBeTruthy()
    expect(screen.getByText(/Cerrado: ya no quedan cupos/i)).toBeTruthy()
    expect(screen.queryByRole('button', { name: /quiero mi cupo/i })).toBeNull()
  })

  it('sin sesión invita a crear cuenta y a volver aquí', async () => {
    pintar(sesion('fuera'))

    const enlace = await screen.findByRole('link', { name: /crear mi cuenta/i })
    expect(enlace.getAttribute('href')).toContain('/crear-cuenta')
    expect(enlace.getAttribute('href')).toContain('planes')
  })

  it('con sesión pero sin ficha de prestador manda a publicarla primero', async () => {
    pintar(sesion('dentro', ['buscador']))

    const enlace = await screen.findByRole('link', { name: /publicar mi ficha/i })
    expect(enlace.getAttribute('href')).toContain('/ofrecer-servicios')
    expect(screen.queryByRole('button', { name: /quiero mi cupo/i })).toBeNull()
  })

  it('si el catálogo falla lo dice y ofrece reintentar', async () => {
    fallaElCatalogo = true
    pintar()

    expect(await screen.findByRole('alert')).toBeTruthy()
    const boton = screen.getByRole('button', { name: /volver a intentar/i })

    fallaElCatalogo = false
    await userEvent.click(boton)

    expect(await screen.findByText('ZASS! Fundador')).toBeTruthy()
  })

  it('tomar el cupo lleva a inscribir el medio de pago', async () => {
    pintar(sesion('dentro', ['prestador']))

    await userEvent.click(await screen.findByRole('button', { name: /quiero mi cupo/i }))

    await waitFor(() => {
      expect(llamadas.filter((r) => r.includes('/api/mi/suscripcion/fundador'))).toHaveLength(1)
    })
  })

  it('dos clics seguidos hacen UNA sola llamada', async () => {
    // Sin esto, el doble clic tomaría dos cupos de los 50.
    let resolver: (r: Response) => void = () => {}
    respuestaDelCupo = () => {
      throw new Error('no se usa')
    }
    vi.stubGlobal('fetch', vi.fn(async (entrada: RequestInfo | URL) => {
      const ruta = String(entrada)
      llamadas.push(ruta)
      if (ruta.includes('/api/auth/csrf')) return json({ token: 't' })
      if (ruta.includes('/api/planes')) {
        return json({ planes: PLANES, cuposFundadorDisponibles: cupos })
      }
      if (ruta.includes('/api/mi/suscripcion/fundador')) {
        return new Promise<Response>((r) => { resolver = r })
      }
      throw new Error('ruta no esperada: ' + ruta)
    }))

    pintar(sesion('dentro', ['prestador']))
    const boton = await screen.findByRole('button', { name: /quiero mi cupo/i })

    await userEvent.click(boton)
    await userEvent.click(boton)

    expect(llamadas.filter((r) => r.includes('/api/mi/suscripcion/fundador'))).toHaveLength(1)
    resolver(json({
      id: 'i1', codigoPlan: 'fundador', precioMensual: 0, descuento: 0, total: 0, cupon: null,
      estado: 'Pendiente', urlParaPagar: '/pago/inscripcion?intento=i1',
      referenciaPasarela: 'ref_1', nueva: true,
    }))
  })

  it('si alguien se llevó el último cupo, la pantalla deja de ofrecerlo', async () => {
    pintar(sesion('dentro', ['prestador']))
    const boton = await screen.findByRole('button', { name: /quiero mi cupo/i })

    // El servidor responde 409 y, al volver a preguntar, ya no quedan cupos.
    respuestaDelCupo = () => json(
      { title: 'Conflicto', detail: 'El plan fundador está cerrado.' }, 409)
    cupos = 0

    await userEvent.click(boton)

    expect(await screen.findByText(/ya están tomados/i)).toBeTruthy()
    expect(screen.queryByRole('button', { name: /quiero mi cupo/i })).toBeNull()
  })

  it('CC-04: el reintento manda la MISMA clave de idempotencia', async () => {
    pintar(sesion('dentro', ['prestador']))
    const boton = await screen.findByRole('button', { name: /quiero mi cupo/i })

    // Primer intento: el servidor falla por algo pasajero.
    respuestaDelCupo = () => json({ title: 'Error', detail: 'La pasarela no respondió.' }, 502)
    await userEvent.click(boton)
    await screen.findByRole('alert')

    // Segundo intento del MISMO envío: tiene que llevar la misma clave, o el servidor lo
    // tomará por una intención distinta y podría tomar un segundo cupo.
    respuestaDelCupo = () => json({
      id: 'i1', codigoPlan: 'fundador', precioMensual: 0, descuento: 0, total: 0, cupon: null,
      estado: 'Pendiente', urlParaPagar: '/pago/inscripcion?intento=i1',
      referenciaPasarela: 'ref_1', nueva: false,
    })
    await userEvent.click(boton)

    await waitFor(() => { expect(claves).toHaveLength(2) })
    expect(claves[0]).not.toBeNull()
    expect(claves[1]).toBe(claves[0])
  })

  it('el cupón muestra los tres números: precio, descuento y total', async () => {
    pintar(sesion('dentro', ['prestador']))

    await userEvent.type(
      await screen.findByRole('textbox', { name: 'Código de descuento' }), 'LANZAMIENTO30')
    await userEvent.click(screen.getByRole('button', { name: /aplicar/i }))

    expect(await screen.findByText('−$2.397')).toBeTruthy()
    expect(screen.getByText('$5.593')).toBeTruthy()
    expect(screen.getByText(/30 %/)).toBeTruthy()
  })

  it('un cupón rechazado dice el motivo y NO borra lo escrito', async () => {
    pintar(sesion('dentro', ['prestador']))

    const campo = await screen.findByRole('textbox', { name: 'Código de descuento' })
    await userEvent.type(campo, 'NOEXISTE')

    respuestaDeCotizar = () => json(
      { title: 'No encontrado', detail: 'Ese código de descuento no existe.' }, 404)

    await userEvent.click(screen.getByRole('button', { name: /aplicar/i }))

    expect(await screen.findByRole('alert')).toBeTruthy()
    // Quien se equivocó en una letra la corrige; no lo reescribe entero.
    expect((campo as HTMLInputElement).value).toBe('NOEXISTE')
  })

  it('a quien no tiene ficha no se le ofrece el cupón', async () => {
    pintar(sesion('dentro', ['buscador']))

    await screen.findByText('ZASS! Fundador')
    expect(screen.queryByRole('textbox', { name: 'Código de descuento' })).toBeNull()
  })
})
