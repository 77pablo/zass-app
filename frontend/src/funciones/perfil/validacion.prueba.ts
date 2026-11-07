import { describe, expect, it } from 'vitest'
import { erroresDelServidor, validarDatos, type DatosEnFormulario } from './validacion'

const bien: DatosEnFormulario = {
  nombrePublico: 'Estudio Espinoza',
  descripcion: '',
  rubroId: '1',
  telefonoPublico: '',
  correoPublico: '',
  whatsapp: '',
  sitioWeb: '',
}

/**
 * Estas reglas ESPEJAN las de ValidadorEditarPerfilPrestador en el backend. No las
 * reemplazan: el servidor manda y vuelve a validar todo. Estan aqui para que la persona se
 * entere al escribir y no despues de pulsar Guardar y esperar una respuesta.
 */
describe('validacion de los datos del perfil', () => {
  it('acepta un perfil con solo el nombre', () => {
    expect(validarDatos(bien)).toEqual({})
  })

  it('exige el nombre, y lo dice como lo dice el servidor', () => {
    expect(validarDatos({ ...bien, nombrePublico: '   ' }).nombrePublico)
      .toMatch(/nombre/i)
  })

  it('limita el nombre a 150 caracteres y la descripcion a 1500', () => {
    expect(validarDatos({ ...bien, nombrePublico: 'a'.repeat(150) }).nombrePublico).toBeUndefined()
    expect(validarDatos({ ...bien, nombrePublico: 'a'.repeat(151) }).nombrePublico).toMatch(/150/)
    expect(validarDatos({ ...bien, descripcion: 'a'.repeat(1500) }).descripcion).toBeUndefined()
    expect(validarDatos({ ...bien, descripcion: 'a'.repeat(1501) }).descripcion).toMatch(/1500/)
  })

  // El regex del backend: movil chileno con o sin +56, con o sin espacios. No fijos: ZASS!
  // contacta por WhatsApp y llamada.
  it.each([
    '+56 9 1234 5678', '+56912345678', '56912345678', '912345678', '9 1234 5678', ' 912345678 ',
  ])('acepta el movil chileno %j', (numero) => {
    expect(validarDatos({ ...bien, telefonoPublico: numero }).telefonoPublico).toBeUndefined()
    expect(validarDatos({ ...bien, whatsapp: numero }).whatsapp).toBeUndefined()
  })

  it.each([
    '221234567',        // fijo de Santiago
    '12345',            // corto
    '+54 9 11 1234 5678', // argentino
    '9123456789',       // un digito de mas
    'nueve uno dos',
  ])('rechaza %j como telefono y como whatsapp', (numero) => {
    expect(validarDatos({ ...bien, telefonoPublico: numero }).telefonoPublico)
      .toMatch(/móvil chileno/i)
    expect(validarDatos({ ...bien, whatsapp: numero }).whatsapp).toMatch(/móvil chileno/i)
  })

  it('deja vacios el telefono y el whatsapp: son opcionales', () => {
    expect(validarDatos({ ...bien, telefonoPublico: '', whatsapp: '   ' })).toEqual({})
  })

  // FluentValidation.EmailAddress() acepta "a@b": solo pide un @ con algo a cada lado. La
  // pantalla es igual de permisiva a proposito: rechazar lo que el servidor acepta
  // dejaria a alguien con un correo valido sin poder guardar.
  it('acepta un correo con forma de correo y rechaza lo que no la tiene', () => {
    expect(validarDatos({ ...bien, correoPublico: 'contacto@espinoza.cl' }).correoPublico).toBeUndefined()
    expect(validarDatos({ ...bien, correoPublico: 'a@b' }).correoPublico).toBeUndefined()
    expect(validarDatos({ ...bien, correoPublico: 'sin-arroba' }).correoPublico).toBeDefined()
    expect(validarDatos({ ...bien, correoPublico: 'con espacio@x.cl' }).correoPublico).toBeDefined()
    expect(validarDatos({ ...bien, correoPublico: `${'a'.repeat(250)}@x.cl` }).correoPublico).toBeDefined()
  })

  it('el sitio web tiene que empezar con http o https', () => {
    expect(validarDatos({ ...bien, sitioWeb: 'https://espinoza.cl' }).sitioWeb).toBeUndefined()
    expect(validarDatos({ ...bien, sitioWeb: 'http://espinoza.cl' }).sitioWeb).toBeUndefined()
    expect(validarDatos({ ...bien, sitioWeb: 'www.espinoza.cl' }).sitioWeb).toMatch(/http/)
    expect(validarDatos({ ...bien, sitioWeb: 'ftp://espinoza.cl' }).sitioWeb).toMatch(/http/)
    // Y no cualquier cosa que empiece parecido: "httpx://" no es http.
    expect(validarDatos({ ...bien, sitioWeb: 'httpx://espinoza.cl' }).sitioWeb).toMatch(/http/)
    expect(validarDatos({ ...bien, sitioWeb: `https://x.cl/${'a'.repeat(300)}` }).sitioWeb).toMatch(/300/)
  })

  it('informa todos los errores a la vez, no uno por uno', () => {
    const errores = validarDatos({
      ...bien, nombrePublico: '', telefonoPublico: '123', sitioWeb: 'nada',
    })

    expect(Object.keys(errores).sort()).toEqual(['nombrePublico', 'sitioWeb', 'telefonoPublico'])
  })
})

describe('errores que devuelve el servidor', () => {
  // El cliente HTTP deja los campos en minuscula ("nombrepublico"). Aqui se llevan de
  // vuelta al nombre del campo del formulario, para mostrarlos donde corresponde.
  it('los asigna al campo del formulario', () => {
    const r = erroresDelServidor({
      nombrepublico: 'Falta el nombre público.',
      telefonopublico: 'Ese teléfono no parece un móvil chileno.',
    })

    expect(r).toEqual({
      nombrePublico: 'Falta el nombre público.',
      telefonoPublico: 'Ese teléfono no parece un móvil chileno.',
    })
  })

  it('ignora campos que el formulario no tiene (version, latitud...)', () => {
    expect(erroresDelServidor({ version: 'x', latitud: 'y' })).toEqual({})
  })
})
