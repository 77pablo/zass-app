import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import type { MiCredencial } from '../funciones/perfil/servicio'
import { hoyISO } from '../marco/texto'
import { MisCredenciales } from './MisCredenciales'

let llamadas: { ruta: string; metodo?: string; cuerpo?: unknown }[]
let respuesta: Response

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), { status: estado, headers: { 'content-type': 'application/json' } })
}

function una(estado: MiCredencial['estado'], nombre = 'Abogado'): MiCredencial {
  return {
    id: `id-${nombre}`, tipo: 'Titulo', nombre, entidad: 'Universidad de La Frontera',
    entidadReconocida: true, obtenidaEn: '2018-03-15', venceEn: null, estado, tieneRespaldo: false, documentos: 0,
  }
}

beforeEach(() => {
  llamadas = []
  respuesta = new Response(null, { status: 204 })
  olvidarCsrf()
  vi.stubGlobal('fetch', vi.fn(async (ruta: string, opciones?: RequestInit) => {
    if (ruta === '/api/auth/csrf') return json({ token: 't' })
    llamadas.push({
      ruta, metodo: opciones?.method,
      cuerpo: opciones?.body ? JSON.parse(String(opciones.body)) : undefined,
    })
    return respuesta
  }))
})

describe('Tus credenciales', () => {
  it('sin ninguna invita a agregar la primera', () => {
    render(<MisCredenciales credenciales={[]} alCambiar={() => {}} />)

    expect(screen.getByText(/le dan confianza a quien te busca/)).toBeInTheDocument()
  })

  it.each([
    ['Borrador', 'Borrador: solo tú la ves'],
    ['EnRevision', 'En revisión'],
    ['Verificada', 'Verificada'],
    ['Rechazada', 'No pudimos verificarla'],
    ['Vencida', 'Vencida'],
  ] as const)('el estado interno %s se dice como «%s»', (estado, texto) => {
    render(<MisCredenciales credenciales={[una(estado)]} alCambiar={() => {}} />)

    expect(screen.getByText(texto, { selector: '.sello, .dato' })).toBeInTheDocument()
  })

  it('un borrador avisa que todavía no se puede enviar a revisión, y sin borradores no lo dice', () => {
    const { rerender } = render(<MisCredenciales credenciales={[una('Borrador')]} alCambiar={() => {}} />)
    expect(screen.getByText(/todavía no puedes subir documentos ni enviarla a revisión/)).toBeInTheDocument()

    rerender(<MisCredenciales credenciales={[una('Verificada')]} alCambiar={() => {}} />)
    expect(screen.queryByText(/todavía no puedes subir documentos/)).not.toBeInTheDocument()
  })

  it('muestra tipo, entidad y año', () => {
    render(<MisCredenciales credenciales={[una('Borrador')]} alCambiar={() => {}} />)

    expect(screen.getByText('Título · Universidad de La Frontera · 2018')).toBeInTheDocument()
  })

  it('crea una credencial con lo escrito, recortado, y avisa para recargar', async () => {
    const u = userEvent.setup()
    const alCambiar = vi.fn()
    render(<MisCredenciales credenciales={[]} alCambiar={alCambiar} />)

    await u.click(screen.getAllByRole('button', { name: /agregar/i })[0])
    await u.selectOptions(screen.getByLabelText('Qué es'), 'Curso')
    await u.type(screen.getByLabelText('Nombre de la credencial'), '  Curso de tasación ')
    await u.type(screen.getByLabelText(/Quién la otorgó/), 'Instituto del Sur')
    await u.type(screen.getByLabelText('Cuándo la obtuviste'), '2021-05-10')
    await u.click(screen.getByRole('button', { name: 'Guardar credencial' }))

    await vi.waitFor(() => expect(alCambiar).toHaveBeenCalledTimes(1))
    expect(llamadas).toEqual([{
      ruta: '/api/mi/credenciales', metodo: 'POST',
      cuerpo: {
        tipo: 'Curso', nombre: 'Curso de tasación', entidadNombre: 'Instituto del Sur',
        obtenidaEn: '2021-05-10', venceEn: null, entidadId: null,
      },
    }])
  })

  it('no manda nada con un nombre corto, sin fecha o con vencimiento anterior', async () => {
    const u = userEvent.setup()
    render(<MisCredenciales credenciales={[]} alCambiar={() => {}} />)
    await u.click(screen.getAllByRole('button', { name: /agregar/i })[0])

    await u.type(screen.getByLabelText('Nombre de la credencial'), 'ab')
    await u.click(screen.getByRole('button', { name: 'Guardar credencial' }))
    expect(screen.getByText(/entre 3 y 150 caracteres/)).toBeInTheDocument()
    expect(screen.getByText('Indica cuándo la obtuviste.')).toBeInTheDocument()

    await u.type(screen.getByLabelText('Nombre de la credencial'), 'c')
    await u.type(screen.getByLabelText('Cuándo la obtuviste'), '2020-06-01')
    await u.type(screen.getByLabelText(/Cuándo vence/), '2020-06-01')
    await u.click(screen.getByRole('button', { name: 'Guardar credencial' }))
    expect(screen.getByText(/posterior a la fecha de obtención/)).toBeInTheDocument()
    expect(llamadas).toHaveLength(0)
  })

  it('una fecha de obtención futura no sale del navegador', async () => {
    const u = userEvent.setup()
    render(<MisCredenciales credenciales={[]} alCambiar={() => {}} />)
    await u.click(screen.getAllByRole('button', { name: /agregar/i })[0])

    await u.type(screen.getByLabelText('Nombre de la credencial'), 'Abogado')
    await u.type(screen.getByLabelText('Cuándo la obtuviste'), '2999-01-01')
    await u.click(screen.getByRole('button', { name: 'Guardar credencial' }))

    expect(screen.getByText('La fecha no puede ser futura.')).toBeInTheDocument()
    expect(llamadas).toHaveLength(0)
  })

  it('el nombre de la entidad se manda sin espacios sobrantes', async () => {
    const u = userEvent.setup()
    render(<MisCredenciales credenciales={[]} alCambiar={() => {}} />)
    await u.click(screen.getAllByRole('button', { name: /agregar/i })[0])
    await u.type(screen.getByLabelText('Nombre de la credencial'), 'Abogado')
    await u.type(screen.getByLabelText(/Quién la otorgó/), '   Instituto del Sur   ')
    await u.type(screen.getByLabelText('Cuándo la obtuviste'), '2020-06-01')
    await u.click(screen.getByRole('button', { name: 'Guardar credencial' }))

    await vi.waitFor(() => expect(llamadas).toHaveLength(1))
    expect((llamadas[0].cuerpo as { entidadNombre: string }).entidadNombre).toBe('Instituto del Sur')
  })

  it('el selector de fecha no deja elegir un día futuro', async () => {
    const u = userEvent.setup()
    render(<MisCredenciales credenciales={[]} alCambiar={() => {}} />)
    await u.click(screen.getAllByRole('button', { name: /agregar/i })[0])

    expect(screen.getByLabelText('Cuándo la obtuviste')).toHaveAttribute('max', hoyISO())
  })

  it('si la API rechaza, muestra su motivo y no cierra el formulario', async () => {
    const u = userEvent.setup()
    const alCambiar = vi.fn()
    respuesta = json({ title: 'x', detail: 'Llegaste al máximo de 25 credenciales.' }, 422)
    render(<MisCredenciales credenciales={[]} alCambiar={alCambiar} />)
    await u.click(screen.getAllByRole('button', { name: /agregar/i })[0])
    await u.type(screen.getByLabelText('Nombre de la credencial'), 'Abogado')
    await u.type(screen.getByLabelText('Cuándo la obtuviste'), '2020-06-01')
    await u.click(screen.getByRole('button', { name: 'Guardar credencial' }))

    expect(await screen.findByText('Llegaste al máximo de 25 credenciales.')).toBeInTheDocument()
    expect(alCambiar).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Guardar credencial' })).toBeEnabled()
  })

  it('eliminar pide confirmar antes de llamar, y avisa que se pierde el sello si lo tenía', async () => {
    const u = userEvent.setup()
    const alCambiar = vi.fn()
    render(<MisCredenciales credenciales={[una('Verificada')]} alCambiar={alCambiar} />)

    await u.click(screen.getByRole('button', { name: /^Eliminar/ }))
    expect(llamadas).toHaveLength(0)
    expect(screen.getByText('Perderás el sello. ¿Eliminar?')).toBeInTheDocument()

    await u.click(screen.getByRole('button', { name: /Sí, eliminar/ }))
    await vi.waitFor(() => expect(alCambiar).toHaveBeenCalledTimes(1))
    expect(llamadas).toEqual([{ ruta: '/api/mi/credenciales/id-Abogado', metodo: 'DELETE', cuerpo: undefined }])
  })

  it('«No» cancela la eliminación sin llamar a la API', async () => {
    const u = userEvent.setup()
    render(<MisCredenciales credenciales={[una('Borrador')]} alCambiar={() => {}} />)

    await u.click(screen.getByRole('button', { name: /^Eliminar/ }))
    expect(screen.getByText('¿Eliminar?')).toBeInTheDocument()
    await u.click(screen.getByRole('button', { name: 'No' }))

    expect(llamadas).toHaveLength(0)
    expect(screen.queryByText('¿Eliminar?')).not.toBeInTheDocument()
  })
})
