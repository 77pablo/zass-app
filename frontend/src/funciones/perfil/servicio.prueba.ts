import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  despublicar, guardarCobertura, guardarDatos, guardarServicios, miCobertura,
  miPerfil, misServicios, miSuscripcion, publicar,
} from './servicio'

const obtener = vi.fn()
const enviar = vi.fn()
vi.mock('../../api/cliente', () => ({
  obtener: (...a: unknown[]) => obtener(...a),
  enviar: (...a: unknown[]) => enviar(...a),
}))

beforeEach(() => {
  obtener.mockReset().mockResolvedValue({})
  enviar.mockReset().mockResolvedValue(undefined)
})

const datos = {
  nombrePublico: 'Estudio Espinoza',
  descripcion: 'Corretaje en Temuco',
  rubroId: 1,
  telefonoPublico: '+56 9 1234 5678',
  correoPublico: 'contacto@espinoza.cl',
  whatsapp: '912345678',
  sitioWeb: 'https://espinoza.cl',
}

describe('lectura', () => {
  it('lee el perfil, los servicios, la cobertura y la suscripcion de quien esta dentro', async () => {
    await miPerfil()
    expect(obtener).toHaveBeenLastCalledWith('/api/mi/perfil-prestador')
    await misServicios()
    expect(obtener).toHaveBeenLastCalledWith('/api/mi/perfil-prestador/servicios')
    await miCobertura()
    expect(obtener).toHaveBeenLastCalledWith('/api/mi/perfil-prestador/cobertura')
    await miSuscripcion()
    expect(obtener).toHaveBeenLastCalledWith('/api/mi/suscripcion')
  })
})

describe('guardar los datos (API-19)', () => {
  it('hace PUT al perfil con la version que se leyo', async () => {
    await guardarDatos(datos, 7)

    const [ruta, cuerpo, metodo] = enviar.mock.calls[0]
    expect(ruta).toBe('/api/mi/perfil-prestador')
    expect(metodo).toBe('PUT')
    expect(cuerpo).toMatchObject({ nombrePublico: 'Estudio Espinoza', rubroId: 1, version: 7 })
  })

  // El formulario no tiene direccion (el geocodificador no existe todavia), pero la API si
  // la acepta. Mandar `direccion: null` es lo que le dice "no toques la ubicacion": si
  // este campo faltara o fuera otra cosa, guardar el nombre podria borrar lo que otra
  // pantalla pusiera mas adelante.
  it('manda direccion, latitud y longitud en nulo para no tocar la ubicacion', async () => {
    await guardarDatos(datos, 0)

    expect(enviar.mock.calls[0][1]).toMatchObject({
      direccion: null, latitud: null, longitud: null,
    })
  })

  // Un campo vacio y un campo ausente no son lo mismo para la API: "" se guardaria como
  // un telefono de cero caracteres. Lo vacio se manda como nulo.
  it('manda como nulo lo que esta vacio o en blanco', async () => {
    await guardarDatos({
      ...datos, descripcion: '   ', telefonoPublico: '', correoPublico: '', whatsapp: '  ', sitioWeb: '',
    }, 0)

    expect(enviar.mock.calls[0][1]).toMatchObject({
      descripcion: null, telefonoPublico: null, correoPublico: null, whatsapp: null, sitioWeb: null,
    })
  })

  it('recorta los espacios de lo que si viene', async () => {
    await guardarDatos({ ...datos, nombrePublico: '  Estudio Espinoza  ' }, 0)

    expect(enviar.mock.calls[0][1].nombrePublico).toBe('Estudio Espinoza')
  })

  it('manda el rubro en nulo cuando todavia no se eligio', async () => {
    await guardarDatos({ ...datos, rubroId: null }, 0)

    expect(enviar.mock.calls[0][1].rubroId).toBeNull()
  })
})

describe('guardar servicios, cobertura y publicar', () => {
  it('guarda la lista completa de servicios con PUT (API-20)', async () => {
    const servicios = [
      { servicioId: 101, duracionMinutos: 60, modoAgenda: 'automatico' as const },
      { nombreNuevo: 'Visita a terreno', duracionMinutos: 45, modoAgenda: 'manual' as const, precioReferencia: 25000 },
    ]

    await guardarServicios(servicios)

    expect(enviar).toHaveBeenCalledWith('/api/mi/perfil-prestador/servicios', { servicios }, 'PUT')
  })

  it('guarda la lista completa de zonas con PUT (API-21)', async () => {
    const zonas = [{ zonaId: 1, esPrincipal: true }, { zonaId: 11, esPrincipal: false }]

    await guardarCobertura(zonas)

    expect(enviar).toHaveBeenCalledWith('/api/mi/perfil-prestador/cobertura', { zonas }, 'PUT')
  })

  it('publica y despublica con POST (API-22), sin cuerpo', async () => {
    await publicar()
    expect(enviar).toHaveBeenLastCalledWith('/api/mi/perfil-prestador/publicar')

    await despublicar()
    expect(enviar).toHaveBeenLastCalledWith('/api/mi/perfil-prestador/despublicar')
  })
})
