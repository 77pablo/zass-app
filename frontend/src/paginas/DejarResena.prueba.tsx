import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import { Contexto, type EstadoDeSesion, type Usuario } from '../sesion/contexto'
import { DejarResena } from './DejarResena'

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

const PERFIL = 'perfil-de-ana'

const FICHA = {
  id: PERFIL, slug: 'ana-gasfiter', nombrePublico: 'Ana Pérez', descripcion: null, urlFoto: null,
  rubro: null, zonas: [], servicios: [], credenciales: [], promedioCalificacion: null,
  totalResenas: 0, contacto: { telefono: null, whatsapp: null, correo: null, sitioWeb: null },
  publicadoEn: null,
}

const ELEGIBLE = {
  elegible: true, contactoId: 'contacto-1', motivo: 'Ninguno', mensaje: null, puedeDesde: null,
}

function resena(extra: Record<string, unknown> = {}) {
  return {
    id: 'resena-1', perfilPrestadorId: PERFIL, calificacion: 5,
    comentario: 'Llegó puntual y dejó todo funcionando, muy recomendable.',
    aspectos: ['puntualidad'], tipo: 'Servicio', estado: 'Publicada', editada: false,
    publicadaEn: '2026-11-12T12:00:00Z',
    // Lejos: el plazo de 72 h sigue abierto salvo que una prueba diga lo contrario.
    editableHasta: '2099-01-01T00:00:00Z',
    ...extra,
  }
}

let elegibilidad: unknown
let llamadas: { ruta: string; metodo: string; cabeceras: Record<string, string>; cuerpo?: Record<string, unknown> }[]
let alPublicar: (cuerpo: Record<string, unknown>) => Response
let alEditar: (cuerpo: Record<string, unknown>) => Response
let alRegistrarContacto: () => Response

beforeEach(() => {
  elegibilidad = ELEGIBLE
  llamadas = []
  alPublicar = () => json(resena(), 201)
  alEditar = (cuerpo) => json(resena({ ...cuerpo, editada: true }), 200)
  alRegistrarContacto = () => json({ id: 'contacto-nuevo' })
  olvidarCsrf()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string, opciones?: RequestInit) => {
    const metodo = opciones?.method ?? 'GET'
    const cuerpo = opciones?.body ? JSON.parse(String(opciones.body)) as Record<string, unknown> : undefined
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    llamadas.push({ ruta, metodo, cabeceras: (opciones?.headers ?? {}) as Record<string, string>, cuerpo })
    if (ruta.startsWith('/api/prestadores/')) return json(FICHA)
    if (ruta.startsWith('/api/mi/elegibilidad')) return json(elegibilidad)
    if (ruta === '/api/contactos') return alRegistrarContacto()
    if (ruta === '/api/resenas') return alPublicar(cuerpo ?? {})
    if (ruta.startsWith('/api/resenas/')) return alEditar(cuerpo ?? {})
    throw new Error(`ruta sin doble: ${ruta}`)
  }))
})

function pintar(sesion: EstadoDeSesion = 'dentro') {
  const usuario: Usuario | null = sesion === 'dentro'
    ? { id: 'u', correo: 'a@b.cl', nombre: 'Luis', correoVerificado: true, roles: ['buscador'] }
    : null
  return render(
    <MemoryRouter initialEntries={['/prestadores/ana-gasfiter/resena']}>
      <Contexto.Provider value={{ estado: sesion, usuario, recargar: async () => {}, salir: async () => {} }}>
        <Routes>
          <Route path="/prestadores/:slug/resena" element={<DejarResena />} />
          <Route path="/entrar" element={<p>pantalla de entrar</p>} />
        </Routes>
      </Contexto.Provider>
    </MemoryRouter>,
  )
}

/** Llena el formulario con algo valido y lo envia. */
async function escribirYEnviar(texto: string, boton = /publicar mi reseña/i) {
  const persona = userEvent.setup()
  await persona.click(await screen.findByRole('radio', { name: '4 estrellas' }))
  const campo = screen.getByRole('textbox', { name: /cuenta cómo fue/i })
  await persona.clear(campo)
  await persona.type(campo, texto)
  await persona.click(screen.getByRole('button', { name: boton }))
  return persona
}

const COMENTARIO = 'Hizo el trabajo rápido y quedó impecable, lo recomiendo.'

describe('dejar una reseña (CU-16)', () => {
  it('sin sesion manda a entrar', async () => {
    pintar('fuera')

    expect(await screen.findByText('pantalla de entrar')).toBeInTheDocument()
  })

  it('pregunta la elegibilidad ANTES de mostrar el formulario', async () => {
    pintar()

    await screen.findByRole('radio', { name: '5 estrellas' })
    expect(llamadas.some((l) => l.ruta.startsWith(`/api/mi/elegibilidad?perfilId=${PERFIL}`))).toBe(true)
  })

  it('no elegible: no hay formulario, y dice que falta y desde cuando', async () => {
    elegibilidad = {
      elegible: false, contactoId: 'c', motivo: 'VentanaDeContacto',
      mensaje: 'Para opinar tienen que pasar 7 días desde tu contacto.',
      puedeDesde: '2026-11-19T12:00:00Z',
    }
    pintar()

    expect(await screen.findByText(/7 días desde tu contacto/)).toBeInTheDocument()
    expect(await screen.findByText(/19 de noviembre/)).toBeInTheDocument()
    expect(screen.queryByRole('radio', { name: '5 estrellas' })).not.toBeInTheDocument()
  })

  it('sin contacto ofrece «ya me atendió», que registra el contacto (API-46)', async () => {
    elegibilidad = {
      elegible: false, contactoId: null, motivo: 'SinContacto',
      mensaje: 'Para dejar una reseña primero tienes que haberte contactado.', puedeDesde: null,
    }
    pintar()
    const persona = userEvent.setup()

    await persona.click(await screen.findByRole('button', { name: /ya me atendió/i }))

    await waitFor(() => {
      expect(llamadas.some((l) => l.ruta === '/api/contactos' && l.metodo === 'POST')).toBe(true)
    })
    const registro = llamadas.find((l) => l.ruta === '/api/contactos')
    expect(registro?.cuerpo).toMatchObject({ perfilId: PERFIL, origen: 'Ficha' })
  })

  // El boton de autorreseña no existe: ofrecer "ya me atendió" en el propio perfil seria
  // invitar a algo que el servidor rechaza con un mensaje neutro (CU-16 4G).
  it('la autorreseña no ofrece registrar contacto', async () => {
    elegibilidad = {
      elegible: false, contactoId: null, motivo: 'Autoresena',
      mensaje: 'No puedes dejar una reseña en este perfil.', puedeDesde: null,
    }
    pintar()

    await screen.findByText('No puedes dejar una reseña en este perfil.')
    expect(screen.queryByRole('button', { name: /ya me atendió/i })).not.toBeInTheDocument()
  })

  it('un comentario demasiado corto no se envia y dice cuanto falta', async () => {
    pintar()
    await escribirYEnviar('Muy bueno')

    expect(await screen.findByText(/faltan 11 caracteres/i)).toBeInTheDocument()
    expect(llamadas.some((l) => l.ruta === '/api/resenas')).toBe(false)
  })

  it('sin estrellas no se envia', async () => {
    pintar()
    const persona = userEvent.setup()
    const campo = await screen.findByRole('textbox', { name: /cuenta cómo fue/i })
    await persona.type(campo, COMENTARIO)
    await persona.click(screen.getByRole('button', { name: /publicar mi reseña/i }))

    expect(await screen.findByText(/elige cuántas estrellas/i)).toBeInTheDocument()
    expect(llamadas.some((l) => l.ruta === '/api/resenas')).toBe(false)
  })

  it('publica con la calificacion, el comentario y una clave de idempotencia', async () => {
    pintar()
    await escribirYEnviar(COMENTARIO)

    expect(await screen.findByText(/tu reseña está publicada/i)).toBeInTheDocument()
    const envio = llamadas.find((l) => l.ruta === '/api/resenas')
    expect(envio?.cuerpo).toMatchObject({
      contactoId: 'contacto-1', calificacion: 4, comentario: COMENTARIO, enviarIgual: false,
    })
    expect(envio?.cabeceras['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/)
  })

  // CC-04: la clave es de la INTENCION. Dos intentos del mismo envio (el primero fallo) tienen
  // que llevar la MISMA clave, o el servidor los tomaria por dos reseñas distintas.
  it('reintentar tras un fallo reusa la misma clave', async () => {
    alPublicar = () => json({ title: 'Se cayó', detail: 'Intenta de nuevo.' }, 500)
    pintar()
    const persona = await escribirYEnviar(COMENTARIO)

    await screen.findByText('Intenta de nuevo.')
    alPublicar = () => json(resena(), 201)
    await persona.click(screen.getByRole('button', { name: /publicar mi reseña/i }))

    await screen.findByText(/tu reseña está publicada/i)
    const claves = llamadas.filter((l) => l.ruta === '/api/resenas').map((l) => l.cabeceras['Idempotency-Key'])
    expect(claves).toHaveLength(2)
    expect(claves[0]).toBe(claves[1])
  })

  it('el 422 de datos personales señala los fragmentos y deja enviar igual (4E)', async () => {
    alPublicar = (cuerpo) => cuerpo.enviarIgual === true
      ? json(resena(), 201)
      // problem+json, como lo manda el servidor: `title`/`detail` y las extensiones al lado.
      : json({
        title: 'Revisa tu comentario',
        detail: 'Tu comentario parece incluir datos personales.',
        requiereConfirmacion: true, fragmentos: ['+56 9 1234 5678'],
      }, 422)
    pintar()
    const persona = await escribirYEnviar(COMENTARIO)

    expect(await screen.findByText('Tu comentario parece incluir datos personales.')).toBeInTheDocument()
    expect(screen.getByText('+56 9 1234 5678')).toBeInTheDocument()
    expect(screen.queryByText(/tu reseña está publicada/i)).not.toBeInTheDocument()

    await persona.click(screen.getByRole('button', { name: /enviar igual/i }))

    expect(await screen.findByText(/tu reseña está publicada/i)).toBeInTheDocument()
    expect(llamadas.filter((l) => l.ruta === '/api/resenas').at(-1)?.cuerpo).toMatchObject({ enviarIgual: true })
  })

  // Lo peligroso seria que el permiso de "enviar igual" se quedara pegado: alguien corrige el
  // telefono, escribe OTRO, y se publicaria sin que nadie lo mire.
  it('si se corrige el texto tras el aviso, se vuelve a preguntar', async () => {
    alPublicar = (cuerpo) => cuerpo.enviarIgual === true
      ? json(resena(), 201)
      : json({
        detail: 'Tu comentario parece incluir datos personales.',
        requiereConfirmacion: true, fragmentos: ['+56 9 1234 5678'],
      }, 422)
    pintar()
    const persona = await escribirYEnviar(COMENTARIO)
    await screen.findByText('Tu comentario parece incluir datos personales.')

    await persona.type(screen.getByRole('textbox', { name: /cuenta cómo fue/i }), ' Llamar al 9 8765 4321.')
    await persona.click(screen.getByRole('button', { name: /publicar mi reseña/i }))

    await waitFor(() => expect(llamadas.filter((l) => l.ruta === '/api/resenas')).toHaveLength(2))
    expect(llamadas.filter((l) => l.ruta === '/api/resenas').at(-1)?.cuerpo).toMatchObject({ enviarIgual: false })
    expect(screen.queryByText(/tu reseña está publicada/i)).not.toBeInTheDocument()
  })

  it('publicada, se puede editar dentro del plazo (CS-16)', async () => {
    pintar()
    const persona = await escribirYEnviar(COMENTARIO)
    await screen.findByText(/tu reseña está publicada/i)
    expect(screen.getByText(/puedes cambiarla durante/i)).toBeInTheDocument()

    await persona.click(screen.getByRole('button', { name: /editar mi reseña/i }))
    const campo = screen.getByRole('textbox', { name: /cuenta cómo fue/i })
    await persona.clear(campo)
    await persona.type(campo, 'Lo pensé mejor: quedó bien pero se demoró bastante en llegar.')
    await persona.click(screen.getByRole('button', { name: /guardar los cambios/i }))

    await waitFor(() => {
      expect(llamadas.some((l) => l.ruta === '/api/resenas/resena-1' && l.metodo === 'PUT')).toBe(true)
    })
    expect(await screen.findByText(/tu reseña está publicada/i)).toBeInTheDocument()
  })

  // RN-S5-05 y 4D.5: pasado el plazo el boton no aparece. Dejarlo seria prometer una edicion
  // que el servidor va a rechazar despues de que la persona reescriba su texto.
  it('con el plazo vencido no hay boton de editar', async () => {
    alPublicar = () => json(resena({ editableHasta: '2020-01-01T00:00:00Z' }), 201)
    pintar()
    await escribirYEnviar(COMENTARIO)

    await screen.findByText(/tu reseña está publicada/i)
    expect(screen.getByText(/el plazo de 72 horas para editarla ya terminó/i)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /editar mi reseña/i })).not.toBeInTheDocument()
  })
})
