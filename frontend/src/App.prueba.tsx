import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import usuario from '@testing-library/user-event'
import { App } from './App'

const iniciarSesion = vi.fn()
const cerrarSesion = vi.fn()
vi.mock('./funciones/identidad/servicio', () => ({
  iniciarSesion: (...a: unknown[]) => iniciarSesion(...a),
  cerrarSesion: () => cerrarSesion(),
  registrarse: vi.fn(),
  miCuenta: vi.fn(),
}))

const listarRubros = vi.fn()
const listarZonas = vi.fn()
const buscar = vi.fn()
vi.mock('./funciones/busqueda/servicio', () => ({
  listarRubros: () => listarRubros(),
  listarZonas: () => listarZonas(),
  buscar: (...a: unknown[]) => buscar(...a),
}))

beforeEach(() => {
  iniciarSesion.mockReset()
  cerrarSesion.mockReset().mockResolvedValue(undefined)
  listarRubros.mockReset().mockResolvedValue([
    { id: 1, codigo: 'corretaje', nombre: 'Corretaje de propiedades', icono: null },
  ])
  listarZonas.mockReset().mockResolvedValue([
    { id: 1, nombre: 'Centro', sector: 'centro', comuna: 'Temuco', centroLatitud: -38.74, centroLongitud: -72.59 },
  ])
  buscar.mockReset().mockResolvedValue({
    resultados: [], cursorSiguiente: null, ordenadoPorDistancia: false,
    zonaBuscada: 'Centro', aviso: null,
  })
})

/** Los enlaces de arriba. Se buscan DENTRO de la navegacion porque el formulario de
 *  ingreso tambien tiene un boton "Entrar" y no son lo mismo. */
const navegacion = () => within(screen.getByRole('navigation', { name: /principal/i }))

async function entrarComo(roles: string[], correoVerificado = true) {
  iniciarSesion.mockResolvedValueOnce({ usuarioId: 'u1', roles, correoVerificado })
  await usuario.click(navegacion().getByRole('button', { name: /^entrar$/i }))
  await usuario.type(screen.getByLabelText(/correo/i), 'pablo@zass.cl')
  await usuario.type(screen.getByLabelText(/contrase/i), 'una-clave-larga')
  await usuario.click(screen.getByRole('button', { name: /^entrar$/i }))
}

describe('portada', () => {
  // Cambio deliberado: antes abria en "Entrar". La busqueda no pide sesion (P1 de
  // CU-08), asi que es lo primero que ve alguien que nunca inicio sesion.
  it('abre en la búsqueda, que es pública', async () => {
    render(<App />)

    expect(await screen.findByRole('heading', { name: /^buscar$/i })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /^entrar$/i })).not.toBeInTheDocument()
  })

  it('desde la portada se puede ir a entrar y volver a buscar', async () => {
    render(<App />)

    await usuario.click(navegacion().getByRole('button', { name: /^entrar$/i }))
    expect(screen.getByRole('heading', { name: /^entrar$/i })).toBeInTheDocument()

    await usuario.click(navegacion().getByRole('button', { name: /^buscar$/i }))
    expect(screen.getByRole('heading', { name: /^buscar$/i })).toBeInTheDocument()
  })

  // El enlace de la pagina en la que ya estas no se muestra: apuntaria a si mismo, y
  // en las pantallas de ingreso duplicaria el boton "Entrar" del formulario.
  it('no ofrece ir a donde ya se está', async () => {
    render(<App />)

    expect(navegacion().queryByRole('button', { name: /^buscar$/i })).not.toBeInTheDocument()
    await usuario.click(navegacion().getByRole('button', { name: /^entrar$/i }))
    expect(navegacion().queryByRole('button', { name: /^entrar$/i })).not.toBeInTheDocument()
  })

  // Quien busca, ve resultados y se va a iniciar sesion no puede perder lo que habia
  // buscado al volver: seria empezar de cero por haber hecho una pausa.
  it('al volver a la búsqueda conserva lo que se había buscado', async () => {
    buscar.mockResolvedValue({
      resultados: [{
        perfilId: 'p1', slug: 'estudio', nombrePublico: 'Estudio Espinoza', descripcion: null,
        urlFoto: null, rubroId: 1, zonaPrincipal: 'Centro', distancia: null,
        tieneCredencialVerificada: false, promedioCalificacion: null, totalResenas: 0,
        esTuPerfil: false,
      }],
      cursorSiguiente: null, ordenadoPorDistancia: false, zonaBuscada: 'Centro', aviso: null,
    })
    render(<App />)
    await screen.findByRole('option', { name: 'Corretaje de propiedades' })
    await usuario.selectOptions(screen.getByLabelText(/qué necesitas/i), 'corretaje')
    await usuario.selectOptions(screen.getByLabelText(/en qué zona/i), '1')
    await usuario.click(screen.getByRole('button', { name: /^busca/i }))
    await screen.findByRole('heading', { name: 'Estudio Espinoza' })

    await usuario.click(navegacion().getByRole('button', { name: /^entrar$/i }))
    await usuario.click(navegacion().getByRole('button', { name: /^buscar$/i }))

    expect(screen.getByRole('heading', { name: 'Estudio Espinoza' })).toBeInTheDocument()
    // Y no volvio a pedir el catalogo ni a buscar: seguia ahi, solo escondido.
    expect(listarRubros).toHaveBeenCalledTimes(1)
    expect(buscar).toHaveBeenCalledTimes(1)
  })
})

describe('entrar y crear cuenta', () => {
  it('se puede pasar a crear cuenta y volver', async () => {
    render(<App />)
    await usuario.click(navegacion().getByRole('button', { name: /^entrar$/i }))

    await usuario.click(screen.getByRole('button', { name: /crear una/i }))
    expect(screen.getByRole('heading', { name: /crear cuenta/i })).toBeInTheDocument()

    await usuario.click(screen.getByRole('button', { name: /^entrar$/i }))
    expect(screen.getByRole('heading', { name: /^entrar$/i })).toBeInTheDocument()
  })

  it('tras entrar saluda y ofrece salir', async () => {
    render(<App />)

    await entrarComo(['prestador'])

    expect(await screen.findByText(/prestador/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /salir/i })).toBeInTheDocument()
  })

  // RN-S1: hasta no verificar el correo la cuenta esta a medias. Si la pantalla
  // no lo dice, la persona se entera cuando algo le falla sin explicacion.
  it('avisa si el correo todavía no está verificado', async () => {
    render(<App />)

    await entrarComo(['buscador'], false)

    expect(await screen.findByText(/sin verificar/i)).toBeInTheDocument()
  })
})

describe('con la sesión iniciada', () => {
  it('el acceso de la portada pasa a ser "Mi cuenta" y lleva a la cuenta', async () => {
    render(<App />)
    await entrarComo(['buscador'])
    await screen.findByRole('button', { name: /salir/i })

    await usuario.click(navegacion().getByRole('button', { name: /^buscar$/i }))

    expect(navegacion().queryByRole('button', { name: /^entrar$/i })).not.toBeInTheDocument()
    await usuario.click(navegacion().getByRole('button', { name: /mi cuenta/i }))
    expect(screen.getByRole('button', { name: /salir/i })).toBeInTheDocument()
  })

  it('al salir vuelve a la búsqueda y el acceso vuelve a ser "Entrar"', async () => {
    render(<App />)
    await entrarComo(['buscador'])

    await usuario.click(await screen.findByRole('button', { name: /salir/i }))

    expect(await screen.findByRole('heading', { name: /^buscar$/i })).toBeInTheDocument()
    expect(navegacion().getByRole('button', { name: /^entrar$/i })).toBeInTheDocument()
    expect(cerrarSesion).toHaveBeenCalledTimes(1)
  })

  // Aunque la API falle al cerrar, aqui se sale igual: dejar la pantalla como si
  // siguiera dentro es peor que cerrar de mas.
  it('sale igual aunque la API falle al cerrar la sesión', async () => {
    cerrarSesion.mockRejectedValue(new Error('sin red'))
    render(<App />)
    await entrarComo(['buscador'])

    await usuario.click(await screen.findByRole('button', { name: /salir/i }))

    expect(await screen.findByRole('heading', { name: /^buscar$/i })).toBeInTheDocument()
    expect(navegacion().getByRole('button', { name: /^entrar$/i })).toBeInTheDocument()
  })
})
