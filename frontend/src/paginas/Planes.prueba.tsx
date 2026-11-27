import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
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
    id: 2, codigo: 'mensual', nombre: 'ZASS! Esencial', descripcion: 'El plan de siempre.',
    precioMensual: 7990, diasPrueba: 0, maxServicios: 5, maxTarjetas: 1,
    permiteCredenciales: true, maxFotosTrabajos: 0, permiteEstadisticas: false,
    verificacionPrioritaria: false,
  },
  {
    id: 3, codigo: 'fundador', nombre: 'ZASS! Fundador', descripcion: 'Precio de lanzamiento.',
    precioMensual: 5990, diasPrueba: 0, maxServicios: 15, maxTarjetas: 3,
    permiteCredenciales: true, maxFotosTrabajos: 12, permiteEstadisticas: true,
    verificacionPrioritaria: true,
  },
  {
    id: 4, codigo: 'profesional', nombre: 'ZASS! Profesional', descripcion: 'Para quien vive de su trabajo.',
    precioMensual: 12990, diasPrueba: 0, maxServicios: 15, maxTarjetas: 3,
    permiteCredenciales: true, maxFotosTrabajos: 12, permiteEstadisticas: true,
    verificacionPrioritaria: true,
  },
]

/** API-69 desde el 22-nov-2026 (N3): el cupo RESERVADO 30 minutos, hasta inscribir la tarjeta. */
const CUPO_TOMADO = {
  numero: 12, mesGratisHasta: null, tarjetaInscrita: false, reservadoHasta: '2026-11-22T17:35:00Z',
  siguiente: 'inscribir',
}

/** GET /api/mi/suscripcion de quien está en la prueba gratis y no tomó cupo. */
const SUSCRIPCION_EN_PRUEBA = {
  plan: 'Prueba', estado: 'Activa', vigente: true, enPrueba: true,
  vigenteHasta: '2026-12-20T12:00:00Z', diasRestantes: 25, renovacionAutomatica: false,
  esFundador: false, tarjetaInscrita: false,
}
/** Fundador con el cupo CONFIRMADO (tarjeta inscrita) y en su mes gratis. */
const SUSCRIPCION_FUNDADOR = {
  ...SUSCRIPCION_EN_PRUEBA, plan: 'ZASS! Fundador', vigenteHasta: '2026-12-25T12:00:00Z',
  renovacionAutomatica: true, esFundador: true, tarjetaInscrita: true,
}

let cupos: number
let fallaElCatalogo: boolean
let llamadas: string[]
let claves: (string | null)[]
let respuestaDelCupo: () => Response
let respuestaDeCotizar: (codigoPlan: string) => Response
let planes: typeof PLANES
let cotizados: { codigoPlan: string, codigoCupon?: string }[]
let cobros: { codigoPlan: string, codigoCupon?: string, clave: string | null }[]
let respuestaDelCobro: () => Response
let respuestaDeSuscripcion: () => Response

beforeEach(() => {
  cupos = 37
  fallaElCatalogo = false
  llamadas = []
  claves = []
  respuestaDelCupo = () => json(CUPO_TOMADO)
  // Por omisión, un prestador en su prueba de 30 días: todavía NO es fundador.
  respuestaDeSuscripcion = () => json(SUSCRIPCION_EN_PRUEBA)
  planes = PLANES
  cotizados = []
  cobros = []
  respuestaDelCobro = () => json({
    id: 'c1', codigoPlan: 'profesional', precioMensual: 12990, descuento: 0, total: 12990,
    cupon: null, estado: 'Pendiente', urlParaPagar: '/pago-simulado/c1', referenciaPasarela: null,
    nueva: true,
  })
  respuestaDeCotizar = (codigoPlan) => json({
    codigoPlan, nombrePlan: 'ZASS!', precioMensual: 7990,
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
        : json({ planes, cuposFundadorDisponibles: cupos })
    }
    if (ruta.includes('/api/mi/suscripcion/fundador')) return respuestaDelCupo()
    if (ruta.endsWith('/api/mi/suscripcion')) return respuestaDeSuscripcion()
    if (ruta.includes('/api/mi/cobros/cotizar')) {
      const cuerpo = JSON.parse(String(opciones?.body)) as { codigoPlan: string, codigoCupon?: string }
      cotizados.push(cuerpo)
      return respuestaDeCotizar(cuerpo.codigoPlan)
    }
    if (ruta.endsWith('/api/mi/cobros')) {
      const cuerpo = JSON.parse(String(opciones?.body)) as { codigoPlan: string, codigoCupon?: string }
      cobros.push({ ...cuerpo, clave: new Headers(opciones?.headers).get('Idempotency-Key') })
      return respuestaDelCobro()
    }
    if (ruta.includes('/api/mi/suscripcion/tarjeta')) {
      return json({
        id: 'i1', codigoPlan: 'fundador', montoBruto: 0, montoDescuento: 0, monto: 0,
        codigoCupon: null, estado: 'Pendiente', urlParaPagar: '/pago-simulado/i1', nueva: true,
      })
    }
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

/** Muestra a qué ruta se navegó (la pantalla de pago simulada no existe en esta prueba). */
function Ruta() {
  return <p>RUTA:{useLocation().pathname}</p>
}

function pintar(valor = sesion('fuera')) {
  return render(
    <MemoryRouter>
      <Contexto.Provider value={valor}>
        <Routes>
          <Route path="/" element={<Planes />} />
          <Route path="*" element={<Ruta />} />
        </Routes>
      </Contexto.Provider>
    </MemoryRouter>,
  )
}

describe('Planes', () => {
  it('muestra los planes con su precio y cuántos cupos quedan', async () => {
    pintar()

    expect(await screen.findByText('ZASS! Profesional')).toBeTruthy()
    expect(screen.getByText('ZASS! Esencial')).toBeTruthy()
    expect(screen.getByText('$12.990', { selector: 'strong' })).toBeTruthy()
    expect(screen.getByText('$7.990', { selector: 'strong' })).toBeTruthy()
    expect(screen.getByText('37')).toBeTruthy()
  })

  it('JQ-17: las tres columnas van Esencial, Profesional, Equipo; «El más elegido» solo en el Profesional', async () => {
    pintar()
    await screen.findByText('ZASS! Profesional')

    const columnas = screen.getAllByRole('region')
      .filter((r) => r.className.includes('plan-columna'))
    expect(columnas.map((c) => c.getAttribute('aria-labelledby'))).toEqual([
      'plan-mensual', 'plan-profesional', 'plan-equipo',
    ])
    const conEtiqueta = columnas.filter((c) => within(c).queryByText('El más elegido') !== null)
    expect(conEtiqueta).toHaveLength(1)
    expect(conEtiqueta[0].getAttribute('aria-labelledby')).toBe('plan-profesional')
    // Las columnas se nombran por su título: son regiones de verdad para el lector de pantalla.
    expect(screen.getByRole('region', { name: 'ZASS! Profesional' })).toBeTruthy()
  })

  it('JQ-17: las características salen de la API, no están escritas a mano', async () => {
    pintar()
    const esencial = await screen.findByRole('region', { name: 'ZASS! Esencial' })

    // Tal como viene el mock, el Esencial NO tiene estadísticas: se marca como no incluida.
    expect(within(esencial).getByText('Estadísticas de tu ficha').closest('li')?.textContent)
      .toContain('No incluye')
    expect(within(esencial).getByText('Hasta 5 servicios en tu ficha')).toBeTruthy()
    expect(within(esencial).getByText('1 tarjeta NFC activa')).toBeTruthy()

    // Si la API le da estadísticas al Esencial, la pantalla lo muestra sin tocar el código.
    cleanup()
    planes = PLANES.map((p) => p.codigo === 'mensual'
      ? { ...p, permiteEstadisticas: true, maxFotosTrabajos: 4 }
      : p)
    pintar()
    const esencial2 = await screen.findByRole('region', { name: 'ZASS! Esencial' })
    expect(within(esencial2).getByText('Estadísticas de tu ficha').closest('li')?.textContent)
      .toMatch(/^Incluye/)
    expect(within(esencial2).getByText('Fotos de tus trabajos (hasta 4)')).toBeTruthy()

    const profesional = screen.getByRole('region', { name: 'ZASS! Profesional' })
    expect(within(profesional).getByText('3 tarjetas NFC activas')).toBeTruthy()
    expect(within(profesional).getByText('Verificación prioritaria').closest('li')?.textContent)
      .toMatch(/^Incluye/)
  })

  it('JQ-17: el Equipo dice «Próximamente» y no tiene botón de contratar', async () => {
    pintar(sesion('dentro', ['prestador']))
    const equipo = await screen.findByRole('region', { name: 'ZASS! Equipo' })

    expect(within(equipo).getByText('Próximamente')).toBeTruthy()
    expect(within(equipo).getByText('$24.990')).toBeTruthy()
    expect(within(equipo).queryByRole('button')).toBeNull()
    expect(within(equipo).queryByRole('link')).toBeNull()
    // Los otros dos sí se pueden contratar.
    expect(screen.getAllByRole('button', { name: 'Contratar' })).toHaveLength(2)
  })

  it('JQ-17: «Contratar» el Profesional inicia el cobro con su clave y va a la URL de pago', async () => {
    pintar(sesion('dentro', ['prestador']))
    const profesional = await screen.findByRole('region', { name: 'ZASS! Profesional' })

    await userEvent.click(within(profesional).getByRole('button', { name: 'Contratar' }))

    await waitFor(() => expect(cobros).toHaveLength(1))
    expect(cobros[0].codigoPlan).toBe('profesional')
    expect(cobros[0].clave).toMatch(/^clave-de-prueba-/)
    expect(cobros[0].codigoCupon).toBeUndefined()
    expect(await screen.findByText('RUTA:/pago-simulado/c1')).toBeTruthy()
  })

  it('JQ-17: el cupón se cotiza para el plan elegido (Profesional por defecto, Esencial al cambiar) y solo viaja a ese plan', async () => {
    pintar(sesion('dentro', ['prestador']))
    const campo = await screen.findByRole('textbox', { name: 'Código de descuento' })
    expect((screen.getByRole('radio', { name: 'ZASS! Profesional' }) as HTMLInputElement).checked)
      .toBe(true)

    await userEvent.type(campo, 'LANZAMIENTO30')
    await userEvent.click(screen.getByRole('button', { name: /aplicar/i }))
    await screen.findByText('−$2.397')
    expect(cotizados.map((c) => c.codigoPlan)).toEqual(['profesional'])

    // Con la cotización del Profesional, el cupón NO viaja al Esencial...
    await userEvent.click(within(screen.getByRole('region', { name: 'ZASS! Esencial' }))
      .getByRole('button', { name: 'Contratar' }))
    await waitFor(() => expect(cobros).toHaveLength(1))
    expect(cobros[0].codigoPlan).toBe('mensual')
    expect(cobros[0].codigoCupon).toBeUndefined()
  })

  it('JQ-17: el cupón cotizado viaja al contratar ese mismo plan', async () => {
    pintar(sesion('dentro', ['prestador']))
    await userEvent.type(
      await screen.findByRole('textbox', { name: 'Código de descuento' }), 'LANZAMIENTO30')
    await userEvent.click(screen.getByRole('button', { name: /aplicar/i }))
    await screen.findByText('−$2.397')

    await userEvent.click(within(screen.getByRole('region', { name: 'ZASS! Profesional' }))
      .getByRole('button', { name: 'Contratar' }))
    await waitFor(() => expect(cobros).toHaveLength(1))
    expect(cobros[0].codigoPlan).toBe('profesional')
    expect(cobros[0].codigoCupon).toBe('LANZAMIENTO30')
  })

  it('JQ-17: al cambiar de plan la cotización vieja se va y la nueva sale con el Esencial', async () => {
    pintar(sesion('dentro', ['prestador']))
    await userEvent.type(
      await screen.findByRole('textbox', { name: 'Código de descuento' }), 'LANZAMIENTO30')
    await userEvent.click(screen.getByRole('button', { name: /aplicar/i }))
    await screen.findByText('−$2.397')

    await userEvent.click(screen.getByRole('radio', { name: 'ZASS! Esencial' }))
    expect(screen.queryByText('−$2.397')).toBeNull()
    await userEvent.click(screen.getByRole('button', { name: /aplicar/i }))
    await screen.findByText('−$2.397')
    expect(cotizados.map((c) => c.codigoPlan)).toEqual(['profesional', 'mensual'])
  })

  it('JQ-17: el aviso del fundador usa los precios de la API y los cupos que quedan', async () => {
    pintar()
    const aviso = await screen.findByText(/El plan de \$12\.990, a/)
    expect(aviso.textContent).toContain('$5.990 para siempre')
    expect(aviso.textContent).toContain('Quedan 37 de 50.')

    // Con otros precios en la API, el aviso cambia: no están escritos a mano.
    cleanup()
    planes = PLANES.map((p) => p.codigo === 'profesional' ? { ...p, precioMensual: 14990 } : p)
    pintar()
    expect(await screen.findByText(/El plan de \$14\.990, a/)).toBeTruthy()

    // Sin cupos, el aviso se reemplaza por el texto de cerrado.
    cleanup()
    cupos = 0
    pintar()
    expect(await screen.findByText(/ya están tomados/i)).toBeTruthy()
    expect(screen.queryByText(/para siempre/)).toBeNull()
  })

  it('JQ-17: dice que ningún plan compra un mejor lugar en la búsqueda', async () => {
    pintar()
    expect(await screen.findByText('Ningún plan compra un mejor lugar en la búsqueda.')).toBeTruthy()
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

    expect(await screen.findByText('ZASS! Profesional')).toBeTruthy()
  })

  it('N3: tomar el cupo lo RESERVA, dice hasta qué hora y pide inscribir la tarjeta', async () => {
    pintar(sesion('dentro', ['prestador']))

    await userEvent.click(await screen.findByRole('button', { name: /quiero mi cupo/i }))

    // 17:35 UTC son las 14:35 en Chile (verano, UTC-3).
    expect(await screen.findByText(/Tu cupo N° 12 está reservado hasta las 14:35/)).toBeInTheDocument()
    expect(screen.getByText(/el cupo vuelve a quedar libre para otra persona/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /quiero mi cupo/i })).toBeNull()
    expect(llamadas.filter((r) => r.includes('/api/mi/suscripcion/fundador'))).toHaveLength(1)
    // La tarjeta se inscribe cuando la persona lo pide, no sola.
    expect(llamadas.some((r) => r.includes('/suscripcion/tarjeta'))).toBe(false)
  })

  it('N3: «Inscribir mi tarjeta» abre la inscripción (API-76) que confirma el cupo', async () => {
    pintar(sesion('dentro', ['prestador']))
    await userEvent.click(await screen.findByRole('button', { name: /quiero mi cupo/i }))

    await userEvent.click(await screen.findByRole('button', { name: /inscribir mi tarjeta/i }))

    await waitFor(() =>
      expect(llamadas.filter((r) => r.includes('/api/mi/suscripcion/tarjeta'))).toHaveLength(1))
  })

  it('quien tomó el cupo SIN mes gratis (siguiente=pagar) ve «Pagar» y no «Inscribir mi tarjeta»', async () => {
    respuestaDelCupo = () => json({ ...CUPO_TOMADO, siguiente: 'pagar' })
    pintar(sesion('dentro', ['prestador']))

    await userEvent.click(await screen.findByRole('button', { name: /quiero mi cupo/i }))

    expect(await screen.findByText(/paga tu primer mes del plan fundador/)).toBeInTheDocument()
    expect(screen.getByText(/no hay mes gratis/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /inscribir mi tarjeta/i })).toBeNull()
    expect(screen.getByRole('button', { name: /pagar \$5\.990/i })).toBeInTheDocument()
    // Nada se cobra ni se abre solo: el pago empieza cuando la persona lo pide.
    expect(llamadas.some((r) => r.endsWith('/api/mi/cobros'))).toBe(false)
  })

  it('«Pagar» abre el cobro del plan fundador (API-68) y no la inscripción de la tarjeta (API-76)', async () => {
    respuestaDelCupo = () => json({ ...CUPO_TOMADO, siguiente: 'pagar' })
    pintar(sesion('dentro', ['prestador']))
    await userEvent.click(await screen.findByRole('button', { name: /quiero mi cupo/i }))

    await userEvent.click(await screen.findByRole('button', { name: /pagar \$5\.990/i }))

    await waitFor(() =>
      expect(llamadas.filter((r) => r.endsWith('/api/mi/cobros'))).toHaveLength(1))
    expect(llamadas.some((r) => r.includes('/suscripcion/tarjeta'))).toBe(false)
  })

  it('N3: con captcha activo, sin resolverlo no se pide el cupo', async () => {
    vi.stubEnv('VITE_TURNSTILE_SITE_KEY', '1x00000000000000000000AA')
    vi.stubGlobal('turnstile', { render: () => 'w1', reset: () => {}, remove: () => {} })
    try {
      pintar(sesion('dentro', ['prestador']))
      await userEvent.click(await screen.findByRole('button', { name: /quiero mi cupo/i }))

      expect(await screen.findByText(/completa la verificación de que eres una persona/)).toBeInTheDocument()
      expect(llamadas.some((r) => r.includes('/api/mi/suscripcion/fundador'))).toBe(false)
    } finally {
      vi.unstubAllEnvs()
    }
  })

  it('N3: con el captcha resuelto, el token viaja en la cabecera X-Turnstile-Token', async () => {
    vi.stubEnv('VITE_TURNSTILE_SITE_KEY', '1x00000000000000000000AA')
    const opcionesDelWidget: Record<string, unknown>[] = []
    vi.stubGlobal('turnstile', {
      render: (_caja: HTMLElement, o: Record<string, unknown>) => {
        opcionesDelWidget.push(o)
        ;(o.callback as (t: string) => void)('token-resuelto')
        return 'w1'
      },
      reset: () => {}, remove: () => {},
    })
    const cabeceras: (string | null)[] = []
    const fetchOriginal = globalThis.fetch
    vi.stubGlobal('fetch', vi.fn(async (entrada: RequestInfo | URL, opciones?: RequestInit) => {
      if (String(entrada).includes('/api/mi/suscripcion/fundador'))
        cabeceras.push(new Headers(opciones?.headers).get('X-Turnstile-Token'))
      return fetchOriginal(entrada, opciones)
    }))
    try {
      pintar(sesion('dentro', ['prestador']))
      await waitFor(() => expect(opcionesDelWidget).toHaveLength(1))
      expect(opcionesDelWidget[0].action).toBe('cupo_fundador')

      await userEvent.click(await screen.findByRole('button', { name: /quiero mi cupo/i }))

      await waitFor(() => expect(cabeceras).toEqual(['token-resuelto']))
    } finally {
      vi.unstubAllEnvs()
    }
  })

  it('antes de tomarlo dice que se guarda 30 minutos y que hoy no se cobra', async () => {
    pintar(sesion('dentro', ['prestador']))

    expect(await screen.findByText(/Te guardamos el cupo 30 minutos/)).toBeInTheDocument()
    expect(screen.getByText(/Hoy no se\s+te cobra nada/)).toBeInTheDocument()
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
    resolver(json(CUPO_TOMADO))
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
    respuestaDelCupo = () => json(CUPO_TOMADO)
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

    await screen.findByText('ZASS! Profesional')
    expect(screen.queryByRole('textbox', { name: 'Código de descuento' })).toBeNull()
  })

  describe('25-nov: quien YA es fundador vuelve a /planes', () => {
    it('no ve «Quiero mi cupo» ni «Contratar»: ve que el cupo ya es suyo y hasta cuándo es gratis', async () => {
      respuestaDeSuscripcion = () => json(SUSCRIPCION_FUNDADOR)
      pintar(sesion('dentro', ['prestador']))

      expect(await screen.findByText('Ya tienes tu cupo de fundador.')).toBeInTheDocument()
      expect(screen.getByText(/Tu mes gratis dura hasta el/)).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: /quiero mi cupo/i })).toBeNull()
      expect(screen.queryByRole('button', { name: /contratar/i })).toBeNull()
      expect(screen.getAllByText('Ya tienes el plan fundador.')).toHaveLength(2)
    })

    it('pasado el mes gratis dice el precio que paga, no una fecha de prueba', async () => {
      respuestaDeSuscripcion = () => json({ ...SUSCRIPCION_FUNDADOR, enPrueba: false })
      pintar(sesion('dentro', ['prestador']))

      expect(await screen.findByText(/Pagas \$5\.990 al mes, para siempre/)).toBeInTheDocument()
      expect(screen.queryByText(/mes gratis dura/)).toBeNull()
    })

    it('con los 50 cupos tomados, al fundador no se le dice «Cerrado»: el suyo sigue', async () => {
      cupos = 0
      respuestaDeSuscripcion = () => json(SUSCRIPCION_FUNDADOR)
      pintar(sesion('dentro', ['prestador']))

      expect(await screen.findByText('Ya tienes tu cupo de fundador.')).toBeInTheDocument()
      expect(screen.queryByText(/Cerrado: ya no quedan cupos/)).toBeNull()
    })

    it('un fundador VENCIDO no cuenta como fundador: se le vuelve a ofrecer el cupo', async () => {
      respuestaDeSuscripcion = () => json({ ...SUSCRIPCION_FUNDADOR, vigente: false })
      pintar(sesion('dentro', ['prestador']))

      await waitFor(() => expect(llamadas.some((r) => r.endsWith('/api/mi/suscripcion'))).toBe(true))
      expect(await screen.findByRole('button', { name: /quiero mi cupo/i })).toBeInTheDocument()
      expect(screen.queryByText('Ya tienes tu cupo de fundador.')).toBeNull()
    })

    it('quien está en la prueba y NO es fundador sigue viendo «Quiero mi cupo» y «Contratar»', async () => {
      pintar(sesion('dentro', ['prestador']))

      expect(await screen.findByRole('button', { name: /quiero mi cupo/i })).toBeInTheDocument()
      await waitFor(() => expect(llamadas.some((r) => r.endsWith('/api/mi/suscripcion'))).toBe(true))
      expect(screen.getAllByRole('button', { name: /contratar/i })).toHaveLength(2)
      expect(screen.queryByText('Ya tienes tu cupo de fundador.')).toBeNull()
    })

    it('si /api/mi/suscripcion falla, la pantalla queda como antes (ofrece el cupo)', async () => {
      respuestaDeSuscripcion = () => json({ title: 'Error' }, 500)
      pintar(sesion('dentro', ['prestador']))

      expect(await screen.findByRole('button', { name: /quiero mi cupo/i })).toBeInTheDocument()
    })

    it('sin sesión no se pide la suscripción', async () => {
      pintar()
      await screen.findByText('ZASS! Profesional')
      expect(llamadas.some((r) => r.endsWith('/api/mi/suscripcion'))).toBe(false)
    })
  })

  it('25-nov: antes de ir a Flow se avisa del cargo de verificación de $50', async () => {
    pintar(sesion('dentro', ['prestador']))
    expect(await screen.findByText(/cargo de verificación de \$50 de «FLOW»/)).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /quiero mi cupo/i }))
    await screen.findByRole('button', { name: /inscribir mi tarjeta/i })
    expect(screen.getAllByText(/cargo de verificación de \$50/).length).toBeGreaterThanOrEqual(1)
  })
})
