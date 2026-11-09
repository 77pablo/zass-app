import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import { olvidarCaches } from '../api/useCargado'
import { Contexto, type EstadoDeSesion, type Usuario } from '../sesion/contexto'
import { MiFicha } from './MiFicha'

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' }})
}

const PERFIL = {
  id: 'p1', slug: 'ana-rojas', nombrePublico: 'Ana Rojas', descripcion: 'Peluquera a domicilio.',
  rubroId: 1, estado: 'Publicado', zonaPrincipal: 'Nunoa', tieneDireccion: false,
  telefonoPublico: null, correoPublico: null, whatsapp: null, sitioWeb: null,
  totalResenas: 0, promedioCalificacion: null, version: 1,
  publicadoEn: '2026-11-01T12:00:00Z', faltaParaPublicar: [],
}
const SERVICIOS = [{
  id: 's1', servicioId: 1, nombre: 'Corte de pelo', esCatalogo: true, duracionMinutos: 30,
  modoAgenda: 'Automatico', precioReferencia: null, activo: true,
}]
const COBERTURA = [{ zonaId: 1, nombre: 'Nunoa', sector: 'Oriente', esPrincipal: true, desdeEn: '2026-11-01T12:00:00Z' }]
const RUBROS = [{ id: 1, nombre: 'peluqueria' }]

const suscripcionDe = (cambios: Record<string, unknown> = {}) => ({
  plan: 'prueba', estado: 'Prueba', vigente: true, enPrueba: true,
  vigenteHasta: '2026-12-12T15:00:00Z', diasRestantes: 12, renovacionAutomatica: false,
  ...cambios,
})
const VENCIDA = { estado: 'Vencida', vigente: false, enPrueba: false, diasRestantes: 0 }

let perfil: Record<string, unknown>
let alPedirSuscripcion: () => Response

beforeEach(() => {
  perfil = PERFIL
  alPedirSuscripcion = () => json(suscripcionDe())
  olvidarCsrf()
  olvidarCaches()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string) => {
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    if (ruta === '/api/mi/perfil-prestador') return json(perfil)
    if (ruta === '/api/mi/perfil-prestador/servicios') return json(SERVICIOS)
    if (ruta === '/api/mi/perfil-prestador/cobertura') return json(COBERTURA)
    if (ruta === '/api/mi/suscripcion') return alPedirSuscripcion()
    if (ruta === '/api/mi/credenciales') return json([])
    if (ruta === '/api/rubros') return json(RUBROS)
    if (ruta === '/api/zonas') return json([])
    return json({}, 404)
  }))
})

function pintar(sesion: EstadoDeSesion = 'dentro', roles = ['buscador', 'prestador']) {
  const usuario: Usuario | null = sesion === 'dentro'
    ? { id: 'u', correo: 'a@b.cl', nombre: 'Ana', correoVerificado: true, roles } : null
  return render(
    <MemoryRouter initialEntries={['/mi-ficha']}>
      <Contexto.Provider value={{ estado: sesion, usuario, recargar: async () => {}, salir: async () => {} }}>
        <Routes>
          <Route path="/mi-ficha" element={<MiFicha />} />
          <Route path="/entrar" element={<p>pantalla de entrar</p>} />
        </Routes>
      </Contexto.Provider>
    </MemoryRouter>,
  )
}

const cargada = () => screen.findByRole('heading', { level: 1, name: 'Ana Rojas' })

describe('mi ficha (prestador)', () => {
  it('sin sesion manda a entrar', async () => {
    pintar('fuera')

    expect(await screen.findByText('pantalla de entrar')).toBeInTheDocument()
  })

  it('una cuenta de buscador no ve la ficha', async () => {
    pintar('dentro', ['buscador'])

    expect(await screen.findByText('Tu cuenta es de quien busca.')).toBeInTheDocument()
  })

  it('publicada con la prueba vigente: sello normal y los dias que quedan', async () => {
    pintar()
    await cargada()

    expect(screen.getByText(/Publicada: la ven/)).toBeInTheDocument()
    expect(screen.getByText(/Publicada: la ven/)).toHaveClass('sello')
    expect(screen.getByText('Prueba \u00b7 12 días')).toBeInTheDocument()
    expect(screen.queryByText(/fuera de las búsquedas/)).not.toBeInTheDocument()
    expect(screen.queryByText(/Suscripción vencida/)).not.toBeInTheDocument()
  })

  it('publicada con la suscripcion vencida: dice que esta fuera de las busquedas y avisa', async () => {
    alPedirSuscripcion = () => json(suscripcionDe(VENCIDA))
    pintar()
    await cargada()

    expect(screen.getByText(/Publicada, pero fuera de las búsquedas/)).toHaveClass('dato')
    expect(screen.getByText(/Publicada, pero fuera de las búsquedas/)).not.toHaveClass('sello')
    expect(screen.getByText(/Publicada, pero fuera de las búsquedas/)).toBeInTheDocument()
    expect(screen.getByText('Suscripción vencida: tu ficha no aparece en las búsquedas')).toBeInTheDocument()
    expect(screen.queryByText(/Publicada: la ven/)).not.toBeInTheDocument()
  })

  it('cancelada lo dice como cancelada, no como vencida', async () => {
    alPedirSuscripcion = () => json(suscripcionDe({ estado: 'Cancelada', vigente: false, enPrueba: false, diasRestantes: 0 }))
    pintar()
    await cargada()

    expect(screen.getByText('Suscripción cancelada: tu ficha no aparece en las búsquedas')).toBeInTheDocument()
    expect(screen.queryByText(/Suscripción vencida/)).not.toBeInTheDocument()
  })

  it('en pausa con la suscripcion vencida sigue diciendo que esta en pausa', async () => {
    perfil = { ...PERFIL, estado: 'Despublicado' }
    alPedirSuscripcion = () => json(suscripcionDe(VENCIDA))
    pintar()
    await cargada()

    expect(screen.getByText('En pausa: nadie la ve')).toBeInTheDocument()
    expect(screen.queryByText(/Publicada, pero fuera/)).not.toBeInTheDocument()
  })

  it('borrador sin suscripcion no muestra nada de suscripcion', async () => {
    perfil = { ...PERFIL, estado: 'Borrador', publicadoEn: null, faltaParaPublicar: ['el rubro'] }
    alPedirSuscripcion = () => json({
      plan: 'ninguno', estado: 'sin suscripcion', vigente: false, enPrueba: false,
      vigenteHasta: null, diasRestantes: 0, renovacionAutomatica: false,
    })
    pintar()
    await cargada()

    expect(screen.getByText('Borrador: nadie la ve todavía')).toBeInTheDocument()
    expect(screen.queryByText(/Suscripción/)).not.toBeInTheDocument()
    expect(screen.queryByText(/fuera de las búsquedas/)).not.toBeInTheDocument()
  })

  it('si falla pedir la suscripcion, la ficha carga igual y sin aviso', async () => {
    alPedirSuscripcion = () => json({ mensaje: 'falla' }, 500)
    pintar()
    await cargada()

    expect(screen.getByText(/Publicada: la ven/)).toBeInTheDocument()
    expect(screen.queryByText(/Suscripción/)).not.toBeInTheDocument()
    expect(screen.queryByText(/No pudimos cargar tu ficha/)).not.toBeInTheDocument()
  })
})