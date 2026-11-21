import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import { olvidarCaches } from '../api/useCargado'
import { Contexto, type EstadoDeSesion, type Usuario } from '../sesion/contexto'
import { Ficha } from './Ficha'
import { MisGuardados } from './MisGuardados'
import { PaginaBuscar } from './PaginaBuscar'
import { Portada } from './Portada'

/**
 * P-2 y P-3: guardar a alguien (CS-08) y lo que la persona suele buscar (CU-04).
 *
 * Lo que se fija aqui: sin cuenta el boton lleva a crearla y vuelve a donde estaba; con cuenta
 * guarda con el token CSRF y, si la API dice que no, vuelve atras y lo dice; la pantalla de
 * guardados quita, guarda la comuna y los oficios sin pisar lo demas, y la portada parte de ahi.
 */

const USUARIO: Usuario = {
  id: 'u1', correo: 'ana@zass.local', nombre: 'Ana', correoVerificado: true, roles: ['buscador'],
}

const FICHA = {
  id: 'p1', slug: 'marcela-soto', nombrePublico: 'Marcela Soto',
  descripcion: null, urlFoto: null,
  rubro: { id: 1, codigo: 'corretaje', nombre: 'Corretaje de propiedades', icono: null },
  zonas: [], servicios: [], credenciales: [],
  promedioCalificacion: null, totalResenas: 0,
  contacto: { telefono: null, whatsapp: null, correo: null, sitioWeb: null },
  publicadoEn: null, suscripcionVigente: true,
}

const FAVORITO = {
  perfilId: 'p1', slug: 'marcela-soto', nombrePublico: 'Marcela Soto', urlFoto: null,
  rubro: 'corretaje de propiedades', zonaPrincipal: 'Centro', tieneCredencialVerificada: true,
  promedioCalificacion: null, totalResenas: 2,
}

const RUBROS = [1, 2, 3, 4, 5, 6].map((id) => ({ id, codigo: `r${id}`, nombre: `oficio ${id}`, icono: null }))
const ZONAS = [{
  id: 7, nombre: 'Temuco', sector: 'temuco', comuna: 'Temuco', centroLatitud: -38.7, centroLongitud: -72.6,
  tipo: 'Comuna', region: 'La Araucanía', padreId: null,
}]

interface Llamada { ruta: string; metodo: string; init?: RequestInit }
let llamadas: Llamada[]
let perfil: { zonaInteresId: number | null; radioPreferidoKm: number; aceptaAvisos: boolean; canalCorreo: boolean; canalPush: boolean; rubrosInteres: number[]; favoritos: string[] }
let estadoAlGuardar: number

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

function api() {
  vi.stubGlobal('fetch', vi.fn(async (ruta: string, init?: RequestInit) => {
    const metodo = init?.method ?? 'GET'
    llamadas.push({ ruta, metodo, init })
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    if (ruta === '/api/prestadores/marcela-soto') return json(FICHA)
    if (ruta === '/api/rubros') return json(RUBROS)
    if (ruta === '/api/zonas') return json(ZONAS)
    if (ruta === '/api/comunidad') return json({ total: 0, porZona: [], porRegion: [], porRubro: [], recienLlegados: [] })
    if (ruta.startsWith('/api/buscar')) {
      return json({
        resultados: [{ ...FAVORITO, descripcion: null, distancia: null, precioDesde: null, rubro: 'Corretaje' }],
        total: 1, pagina: 1, hayMas: false, ordenAplicado: 'Recomendados', conUbicacion: false, aviso: null,
      })
    }
    if (ruta === '/api/mi/perfil-buscador' && metodo === 'GET') return json(perfil)
    if (ruta === '/api/mi/perfil-buscador' && metodo === 'PUT') return new Response(null, { status: 204 })
    if (ruta === '/api/mi/favoritos') return json(perfil.favoritos.length ? [FAVORITO] : [])
    if (ruta.startsWith('/api/mi/favoritos/')) {
      return estadoAlGuardar === 204
        ? new Response(null, { status: 204 })
        : json({ title: 'Error' }, estadoAlGuardar)
    }
    return json({ title: 'No autorizado' }, 401)
  }))
}

/** Donde aterriza el enlace de «crear cuenta»: muestra a donde promete volver. */
export function CrearCuentaFalsa() {
  const { state } = useLocation()
  return <p>Crear cuenta, y volver a {(state as { despues?: string } | null)?.despues}</p>
}

function pintar(entrada: string, estado: EstadoDeSesion = 'dentro') {
  return render(
    <MemoryRouter initialEntries={[entrada]}>
      <Contexto.Provider
        value={{ estado, usuario: estado === 'dentro' ? USUARIO : null, recargar: async () => {}, salir: async () => {} }}
      >
        <Routes>
          <Route path="/" element={<Portada />} />
          <Route path="buscar" element={<PaginaBuscar />} />
          <Route path="prestadores/:slug" element={<Ficha />} />
          <Route path="mis-guardados" element={<MisGuardados />} />
          <Route path="crear-cuenta" element={<CrearCuentaFalsa />} />
          <Route path="entrar" element={<p>Entrar</p>} />
        </Routes>
      </Contexto.Provider>
    </MemoryRouter>,
  )
}

const de = (metodo: string, prefijo: string) => llamadas.filter((l) => l.metodo === metodo && l.ruta.startsWith(prefijo))

beforeEach(() => {
  olvidarCsrf()
  olvidarCaches()
  localStorage.clear()
  llamadas = []
  estadoAlGuardar = 204
  perfil = { zonaInteresId: null, radioPreferidoKm: 25, aceptaAvisos: false, canalCorreo: true, canalPush: true, rubrosInteres: [], favoritos: [] }
  api()
})
afterEach(() => vi.unstubAllGlobals())

describe('guardar a alguien (CS-08)', () => {
  it('sin cuenta, «Guardar» lleva a crearla y promete volver a la ficha; no llama a la API de favoritos', async () => {
    pintar('/prestadores/marcela-soto', 'fuera')

    fireEvent.click(await screen.findByRole('link', { name: 'Guardar (crea tu cuenta gratis)' }))

    expect(await screen.findByText('Crear cuenta, y volver a /prestadores/marcela-soto')).toBeInTheDocument()
    expect(llamadas.some((l) => l.ruta.startsWith('/api/mi/'))).toBe(false)
  })

  it('con cuenta guarda con el token CSRF y el boton pasa a «Guardado»', async () => {
    pintar('/prestadores/marcela-soto')

    const boton = await screen.findByRole('button', { name: /Guardar a Marcela Soto/ })
    expect(boton).toHaveAttribute('aria-pressed', 'false')
    fireEvent.click(boton)

    await waitFor(() => expect(boton).toHaveAttribute('aria-pressed', 'true'))
    expect(boton).toHaveTextContent('Guardado')
    const [put] = de('PUT', '/api/mi/favoritos/')
    expect(put.ruta).toBe('/api/mi/favoritos/p1')
    expect(new Headers(put.init?.headers).get('X-CSRF-Token')).toBe('t')
    expect(screen.getByRole('link', { name: 'Ver tus guardados' })).toHaveAttribute('href', '/mis-guardados')
  })

  it('si la API no lo guarda, el boton vuelve a «Guardar» y se dice por que', async () => {
    estadoAlGuardar = 500
    pintar('/prestadores/marcela-soto')

    const boton = await screen.findByRole('button', { name: /Guardar a Marcela Soto/ })
    fireEvent.click(boton)

    expect(await screen.findByRole('alert')).toHaveTextContent('No pudimos guardarlo. Intenta de nuevo.')
    expect(boton).toHaveAttribute('aria-pressed', 'false')
  })

  it('ya guardado, tocarlo lo quita con DELETE', async () => {
    perfil.favoritos = ['p1']
    pintar('/prestadores/marcela-soto')

    const boton = await screen.findByRole('button', { name: /Guardado a Marcela Soto/ })
    fireEvent.click(boton)

    await waitFor(() => expect(boton).toHaveAttribute('aria-pressed', 'false'))
    expect(de('DELETE', '/api/mi/favoritos/').map((l) => l.ruta)).toEqual(['/api/mi/favoritos/p1'])
  })

  it('en los resultados, cada persona tiene su boton, fuera del enlace de la tarjeta', async () => {
    pintar('/buscar?q=marcela')

    const boton = await screen.findByRole('button', { name: /Guardar a Marcela Soto/ })
    // Un boton dentro de un enlace no se puede usar con el teclado: tiene que ir al lado.
    expect(boton.closest('a')).toBeNull()
    fireEvent.click(boton)
    await waitFor(() => expect(boton).toHaveAttribute('aria-pressed', 'true'))
  })

  it('en los resultados sin cuenta, vuelve a ESTA busqueda despues de crear la cuenta', async () => {
    pintar('/buscar?q=marcela', 'fuera')

    fireEvent.click(await screen.findByRole('link', { name: 'Guardar (crea tu cuenta gratis)' }))

    expect(await screen.findByText('Crear cuenta, y volver a /buscar?q=marcela')).toBeInTheDocument()
  })
})

describe('la pantalla de guardados', () => {
  it('sin sesion manda a entrar', async () => {
    pintar('/mis-guardados', 'fuera')
    expect(await screen.findByText('Entrar')).toBeInTheDocument()
  })

  it('sin nadie guardado lo dice y ofrece buscar', async () => {
    pintar('/mis-guardados')
    expect(await screen.findByText(/Todavía no guardas a nadie/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Buscar a alguien' })).toHaveAttribute('href', '/buscar')
  })

  it('lista a la gente guardada y «Quitar» la saca con DELETE', async () => {
    perfil.favoritos = ['p1']
    pintar('/mis-guardados')

    const lista = await screen.findByRole('list', { name: 'Gente que guardaste' })
    expect(within(lista).getByRole('link', { name: /Marcela Soto/ })).toHaveAttribute('href', '/prestadores/marcela-soto')

    fireEvent.click(within(lista).getByRole('button', { name: /Quitar a Marcela Soto/ }))

    expect(await screen.findByText(/Todavía no guardas a nadie/)).toBeInTheDocument()
    expect(de('DELETE', '/api/mi/favoritos/').map((l) => l.ruta)).toEqual(['/api/mi/favoritos/p1'])
  })

  it('guarda la comuna y los oficios sin pisar el radio ni los avisos', async () => {
    pintar('/mis-guardados')

    // Primero que lleguen las comunas: un `select` no acepta un valor que no esta entre sus opciones.
    await screen.findByRole('option', { name: 'Temuco' })
    fireEvent.change(screen.getByLabelText('Tu comuna'), { target: { value: '7' } })
    fireEvent.click(await screen.findByRole('button', { name: 'Oficio 2' }))
    fireEvent.click(screen.getByRole('button', { name: 'Oficio 4' }))
    fireEvent.click(screen.getByRole('button', { name: 'Guardar lo que busco' }))

    expect(await screen.findByText(/Listo\./)).toBeInTheDocument()
    const [put] = de('PUT', '/api/mi/perfil-buscador')
    expect(JSON.parse(String(put.init?.body))).toEqual({
      zonaInteresId: 7, radioPreferidoKm: 25, aceptaAvisos: false,
      canalCorreo: true, canalPush: true, rubrosInteres: [2, 4],
    })
  })

  it('con cinco oficios elegidos no deja elegir un sexto', async () => {
    perfil.rubrosInteres = [1, 2, 3, 4, 5]
    pintar('/mis-guardados')

    expect(await screen.findByRole('button', { name: 'Oficio 6' })).toBeDisabled()
    expect(screen.getByText(/Ya elegiste 5/)).toBeInTheDocument()
    // Los elegidos se pueden quitar.
    expect(screen.getByRole('button', { name: 'Oficio 1' })).toBeEnabled()
  })
})

describe('la portada parte de lo que la persona suele buscar (CU-04)', () => {
  it('con comuna y oficios guardados, ofrece esas busquedas ya armadas', async () => {
    perfil.zonaInteresId = 7
    perfil.rubrosInteres = [3]
    pintar('/')

    const atajos = await screen.findByRole('list', { name: 'Lo que sueles buscar' })
    expect(within(atajos).getByRole('link', { name: /Gente en Temuco/ })).toHaveAttribute('href', '/buscar?zona=7')
    expect(within(atajos).getByRole('link', { name: 'Oficio 3 en Temuco' }))
      .toHaveAttribute('href', '/buscar?q=oficio%203&zona=7')
  })

  it('sin sesion no pregunta por preferencias ni muestra el bloque', async () => {
    pintar('/', 'fuera')

    await screen.findByRole('heading', { level: 1 })
    await waitFor(() => expect(llamadas.some((l) => l.ruta === '/api/rubros')).toBe(true))
    expect(llamadas.some((l) => l.ruta === '/api/mi/perfil-buscador')).toBe(false)
    expect(screen.queryByRole('list', { name: 'Lo que sueles buscar' })).not.toBeInTheDocument()
  })
})
