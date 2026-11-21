import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import * as T from '../funciones/tarjetas/textos'
import { Contexto, type EstadoDeSesion, type Usuario } from '../sesion/contexto'
import { ActivarTarjeta } from './ActivarTarjeta'
import { MisTarjetas } from './MisTarjetas'
import { TEXTOS_DE_TARJETA } from './textosDeTarjeta'

vi.mock('qrcode-generator', () => ({
  default: () => ({ addData: () => {}, make: () => {}, getModuleCount: () => 21, isDark: () => false }),
}))

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

const SEGMENTO = '7KQ2-9XRM'
const RUTA = `/tarjeta/sin-activar/${SEGMENTO}`
const TOKEN = 'token-csrf-de-prueba'

interface Llamada { ruta: string; metodo: string; cabeceras: Record<string, string>; cuerpo?: string }
let llamadas: Llamada[]
let estadoPerfil: string
let alActivar: () => Response

beforeEach(() => {
  llamadas = []
  estadoPerfil = 'Publicado'
  alActivar = () => json({ mensaje: 'Tarjeta activada', urlFicha: '/prestadores/ana' })
  olvidarCsrf()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string, opciones?: RequestInit) => {
    if (ruta === '/api/auth/csrf') return json({ token: TOKEN })
    const metodo = opciones?.method ?? 'GET'
    llamadas.push({
      ruta, metodo,
      cabeceras: (opciones?.headers ?? {}) as Record<string, string>,
      cuerpo: typeof opciones?.body === 'string' ? opciones.body : undefined,
    })
    if (ruta === '/api/mi/perfil-prestador') return json({ id: 'p', slug: 'ana', nombrePublico: 'Ana', estado: estadoPerfil })
    if (ruta.endsWith('/activar') && metodo === 'POST') return alActivar()
    if (ruta === '/api/auth/sesion/renovar') return json({}, 401)
    if (ruta === '/api/mi/tarjetas') {
      return json([{ id: 't1', urlEstable: SEGMENTO, estado: 'Activa', activadaEn: '2026-11-21T15:00:00Z', desactivadaEn: null }])
    }
    if (ruta.endsWith('/estadisticas')) return json({}, 404)
    return json({}, 404)
  }))
})

// Exportada solo para que oxlint (only-export-components) no la marque: es la sonda de la ruta /entrar.
export function Entrar() {
  const { state } = useLocation()
  return <p>pantalla de entrar, vuelve a {(state as { despues?: string } | null)?.despues}</p>
}

function pintar(
  { sesion = 'dentro', roles = ['buscador', 'prestador'], ruta = RUTA, recargar = async () => {} }:
  { sesion?: EstadoDeSesion; roles?: string[]; ruta?: string; recargar?: () => Promise<void> } = {},
) {
  const usuario: Usuario | null = sesion === 'dentro'
    ? { id: 'u', correo: 'a@b.cl', nombre: 'Ana', correoVerificado: true, roles } : null
  return render(
    <MemoryRouter initialEntries={[ruta]}>
      <Contexto.Provider value={{ estado: sesion, usuario, recargar, salir: async () => {} }}>
        <Routes>
          <Route path="/tarjeta/sin-activar/:segmento" element={<ActivarTarjeta />} />
          <Route path="/mis-tarjetas" element={<MisTarjetas />} />
          <Route path="/entrar" element={<Entrar />} />
        </Routes>
      </Contexto.Provider>
    </MemoryRouter>,
  )
}

const posts = () => llamadas.filter((l) => l.metodo === 'POST')

async function escribirYActivar(codigo: string) {
  await userEvent.type(await screen.findByLabelText(T.ETIQUETA_CODIGO), codigo)
  await userEvent.click(screen.getByRole('button', { name: T.ACTIVAR }))
}

describe('Activar tarjeta (CU-19)', () => {
  it('sin sesión muestra el aviso y lleva a entrar con la vuelta a esta misma pantalla', async () => {
    pintar({ sesion: 'fuera' })

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(TEXTOS_DE_TARJETA['sin-activar'].titulo)
    expect(screen.queryByLabelText(T.ETIQUETA_CODIGO)).toBeNull()
    await userEvent.click(screen.getByRole('link', { name: T.ENTRAR_PARA_ACTIVAR }))

    expect(screen.getByText(`pantalla de entrar, vuelve a ${RUTA}`)).toBeInTheDocument()
    expect(llamadas).toHaveLength(0)
  })

  it('con un segmento que no tiene forma de tarjeta muestra el aviso sin formulario y no pide nada', () => {
    pintar({ ruta: '/tarjeta/sin-activar/no-es-una-tarjeta' })

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(TEXTOS_DE_TARJETA['sin-activar'].titulo)
    expect(screen.queryByLabelText(T.ETIQUETA_CODIGO)).toBeNull()
    expect(llamadas).toHaveLength(0)
  })

  it('a una cuenta que no ofrece servicios se lo dice, sin formulario', () => {
    pintar({ roles: ['buscador'] })

    expect(screen.getByText(T.NO_OFRECE_SERVICIOS)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: T.NO_PRESTADOR_ENLACE })).toHaveAttribute('href', '/ofrecer-servicios')
    expect(screen.queryByLabelText(T.ETIQUETA_CODIGO)).toBeNull()
  })

  it.each(['Borrador', 'Despublicado'])('con la ficha en %s pide publicarla antes y no ofrece gastar un intento', async (e) => {
    estadoPerfil = e
    pintar()

    expect(await screen.findByText(T.FICHA_SIN_PUBLICAR)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: T.IR_A_MI_FICHA })).toHaveAttribute('href', '/mi-ficha')
    expect(screen.queryByLabelText(T.ETIQUETA_CODIGO)).toBeNull()
    expect(posts()).toHaveLength(0)
  })

  it('activa por /api con el token CSRF y el código normalizado, y lleva a Mis tarjetas con el aviso', async () => {
    pintar({ ruta: `/tarjeta/sin-activar/${SEGMENTO.toLowerCase()}` })
    await escribirYActivar('abcd efgh-jkmn')

    expect(await screen.findByText(T.ACTIVADA_OK)).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(T.TITULO)
    const [post] = posts()
    expect(posts()).toHaveLength(1)
    expect(post.ruta).toBe(`/api/mi/tarjetas/${SEGMENTO}/activar`)
    expect(post.cabeceras['X-CSRF-Token']).toBe(TOKEN)
    expect(JSON.parse(post.cuerpo ?? '{}')).toEqual({ codigo: 'ABCD-EFGH-JKMN' })
  })

  it('Mis tarjetas sin venir de una activación no muestra el aviso', async () => {
    pintar({ ruta: '/mis-tarjetas' })

    expect(await screen.findByRole('article', { name: SEGMENTO })).toBeInTheDocument()
    expect(screen.queryByText(T.ACTIVADA_OK)).toBeNull()
  })

  it.each(['', 'ABCD-EFGH', 'ABCD-EFGH-JKMN-P', 'ABCD-EFGH-JKM0'])(
    'un código sin forma (%j) se corrige en la pantalla y no gasta un intento', async (codigo) => {
      pintar()
      if (codigo) await userEvent.type(await screen.findByLabelText(T.ETIQUETA_CODIGO), codigo)
      await userEvent.click(await screen.findByRole('button', { name: T.ACTIVAR }))

      expect(screen.getByText(T.CODIGO_INCOMPLETO)).toBeInTheDocument()
      expect(posts()).toHaveLength(0)
    })

  /**
   * CU-19 4B.3: la API responde igual a todo lo que no sirve, y la pantalla no puede saber más
   * que ella. Aunque el cuerpo trajera otra cosa, se muestra el mismo texto y nada del servidor.
   */
  it.each([
    'El código no es válido.',
    'La tarjeta está bloqueada hasta las 15:00.',
    'Ese código venció.',
    'La tarjeta ya tiene dueño.',
  ])('cualquier 422 dice lo mismo, sin repetir lo que mandó el servidor (%s)', async (detalle) => {
    alActivar = () => json({ title: 'Regla de negocio', detail: detalle, status: 422 }, 422)
    pintar()
    await escribirYActivar('ABCD-EFGH-JKMN')

    expect(await screen.findByRole('alert')).toHaveTextContent(T.CODIGO_NO_VALIDO)
    expect(screen.getByRole('alert').textContent).toBe(T.CODIGO_NO_VALIDO)
    if (detalle !== 'El código no es válido.') expect(screen.queryByText(detalle, { exact: false })).toBeNull()
    expect(screen.getByLabelText(T.ETIQUETA_CODIGO)).toBeInTheDocument()
  })

  it('el límite de intentos (429) pide esperar', async () => {
    alActivar = () => json({ title: 'Demasiadas peticiones', detail: 'Espera unos minutos.', status: 429 }, 429)
    pintar()
    await escribirYActivar('ABCD-EFGH-JKMN')

    expect(await screen.findByRole('alert')).toHaveTextContent(T.MUCHOS_INTENTOS)
  })

  it('un fallo del servidor no culpa al código', async () => {
    alActivar = () => json({ title: 'Error' }, 500)
    pintar()
    await escribirYActivar('ABCD-EFGH-JKMN')

    expect(await screen.findByRole('alert')).toHaveTextContent(T.ERROR_ACTIVAR)
    expect(screen.queryByText(T.CODIGO_NO_VALIDO)).toBeNull()
  })

  it('con la sesión vencida recarga la sesión en vez de mostrar un error del código', async () => {
    alActivar = () => json({}, 401)
    const recargar = vi.fn(async () => {})
    pintar({ recargar })
    await escribirYActivar('ABCD-EFGH-JKMN')

    await waitFor(() => expect(recargar).toHaveBeenCalled())
    expect(screen.queryByText(T.CODIGO_NO_VALIDO)).toBeNull()
  })
})
