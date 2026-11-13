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

  it('un borrador explica qué es la revisión, y sin borradores no lo dice', () => {
    const { rerender } = render(<MisCredenciales credenciales={[una('Borrador')]} alCambiar={() => {}} />)
    expect(screen.getByText(/Envíalo a revisión y lo revisamos a mano/)).toBeInTheDocument()

    rerender(<MisCredenciales credenciales={[una('Verificada')]} alCambiar={() => {}} />)
    expect(screen.queryByText(/Envíalo a revisión/)).not.toBeInTheDocument()
  })

  // Lo que la pantalla decia hasta hoy: que enviar a revision era imposible. API-27 existe desde
  // antes, asi que el sello «Verificada» era inalcanzable solo por este texto y por la falta del
  // boton. El candado es literal a proposito: si alguien vuelve a escribir la frase, cae.
  it('ya no dice que enviarla a revisión sea imposible', () => {
    render(<MisCredenciales credenciales={[una('Borrador')]} alCambiar={() => {}} />)

    expect(screen.queryByText(/todavía no puedes subir documentos ni enviarla a revisión/)).toBeNull()
    expect(screen.queryByText(/no puedes.*enviarla a revisión/)).toBeNull()
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

  describe('enviar a revisión (API-27)', () => {
    it('un borrador se manda con POST a /enviar y sin cuerpo, y avisa para recargar', async () => {
      const u = userEvent.setup()
      const alCambiar = vi.fn()
      render(<MisCredenciales credenciales={[una('Borrador')]} alCambiar={alCambiar} />)

      await u.click(screen.getByRole('button', { name: /^Enviar a revisión/ }))

      await vi.waitFor(() => expect(alCambiar).toHaveBeenCalledTimes(1))
      // La ruta y el metodo son los de API-27 tal cual: POST /api/mi/credenciales/{id}/enviar.
      expect(llamadas).toEqual([
        { ruta: '/api/mi/credenciales/id-Abogado/enviar', metodo: 'POST', cuerpo: undefined },
      ])
    })

    it('el identificador va escapado en la ruta', async () => {
      const u = userEvent.setup()
      const c = { ...una('Borrador'), id: 'a/b c' }
      render(<MisCredenciales credenciales={[c]} alCambiar={() => {}} />)

      await u.click(screen.getByRole('button', { name: /^Enviar a revisión/ }))

      await vi.waitFor(() => expect(llamadas).toHaveLength(1))
      expect(llamadas[0].ruta).toBe('/api/mi/credenciales/a%2Fb%20c/enviar')
    })

    it.each(['EnRevision', 'Verificada', 'Rechazada', 'Vencida'] as const)(
      'una credencial %s no ofrece el botón', (estado) => {
        render(<MisCredenciales credenciales={[una(estado)]} alCambiar={() => {}} />)

        expect(screen.queryByRole('button', { name: /^Enviar a revisión/ })).toBeNull()
      })

    it('mientras va la llamada el botón dice que está enviando y no se puede pulsar de nuevo', async () => {
      const u = userEvent.setup()
      let soltar: (() => void) | undefined
      const espera = new Promise<void>((r) => { soltar = r })
      vi.stubGlobal('fetch', vi.fn(async (ruta: string, opciones?: RequestInit) => {
        if (ruta === '/api/auth/csrf') return json({ token: 't' })
        llamadas.push({ ruta, metodo: opciones?.method })
        await espera
        return respuesta
      }))
      render(<MisCredenciales credenciales={[una('Borrador')]} alCambiar={() => {}} />)

      await u.click(screen.getByRole('button', { name: /^Enviar a revisión/ }))

      const boton = await screen.findByRole('button', { name: /^Enviando…/ })
      expect(boton).toBeDisabled()
      soltar?.()
      await vi.waitFor(() => expect(llamadas).toHaveLength(1))
    })

    // Con la bandera Zass:Verificacion:PermitirSinDocumentos apagada, API-27 contesta 422 con
    // «Sube al menos un documento de respaldo», y subir documentos no existe (MOD-08). Repetir
    // ese mensaje manda a la persona a hacer algo que la pantalla no le ofrece.
    it('si la bandera está apagada explica qué pasa en vez de pedir un documento imposible', async () => {
      const u = userEvent.setup()
      const alCambiar = vi.fn()
      respuesta = json({
        title: 'Regla de negocio',
        detail: 'Sube al menos un documento de respaldo antes de enviar a revision.',
      }, 422)
      render(<MisCredenciales credenciales={[una('Borrador')]} alCambiar={alCambiar} />)

      await u.click(screen.getByRole('button', { name: /^Enviar a revisión/ }))

      expect(await screen.findByText(/subir archivos aún no está disponible/)).toBeInTheDocument()
      expect(screen.getByText(/sigue guardada como borrador/)).toBeInTheDocument()
      expect(screen.queryByText(/Sube al menos un documento/)).toBeNull()
      expect(alCambiar).not.toHaveBeenCalled()
      expect(screen.getByRole('button', { name: /^Enviar a revisión/ })).toBeEnabled()
    })

    it('cualquier otro rechazo muestra el motivo del servidor', async () => {
      const u = userEvent.setup()
      respuesta = json({ title: 'Conflicto', detail: 'Esta credencial ya está en revisión.' }, 409)
      render(<MisCredenciales credenciales={[una('Borrador')]} alCambiar={() => {}} />)

      await u.click(screen.getByRole('button', { name: /^Enviar a revisión/ }))

      expect(await screen.findByText('Esta credencial ya está en revisión.')).toBeInTheDocument()
    })

    it('el error se muestra junto a la credencial que falló, no junto a la otra', async () => {
      const u = userEvent.setup()
      respuesta = json({ title: 'Conflicto', detail: 'Esta credencial ya está en revisión.' }, 409)
      const uno = una('Borrador', 'Abogado')
      const dos = una('Borrador', 'Tasador')
      render(<MisCredenciales credenciales={[uno, dos]} alCambiar={() => {}} />)

      await u.click(screen.getByRole('button', { name: /^Enviar a revisión\s?Tasador/ }))

      const error = await screen.findByText('Esta credencial ya está en revisión.')
      const fila = error.closest('li')
      expect(fila?.textContent).toContain('Tasador')
      expect(fila?.textContent).not.toContain('Abogado')
    })
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
