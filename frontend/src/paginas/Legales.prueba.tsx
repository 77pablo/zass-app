import { afterEach, describe, expect, it } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { Pie } from '../marco/Pie'
import { Contexto, type EstadoDeSesion, type Usuario } from '../sesion/contexto'
import { Registrarse } from '../funciones/identidad/Registrarse'
import { AvisoDeBorrador } from './AvisoDeBorrador'
import { DatoLegal } from './DatoLegal'
import { Privacidad } from './Privacidad'
import { Terminos } from './Terminos'
import { BORRADOR_LEGAL, CORREO_PRIVACIDAD, estaPendiente, RESPONSABLE } from './legal'

afterEach(() => { document.head.innerHTML = ''; document.title = '' })

const enRuta = (ui: React.ReactElement) => render(<MemoryRouter>{ui}</MemoryRouter>)

const DENTRO: Usuario = {
  id: 'u1', correo: 'ana@zass.local', nombre: 'Ana', correoVerificado: true, roles: ['usuario'],
}

/** El pie lee la sesion, asi que necesita el contexto: sin el, `useSesion` lanza a proposito. */
const elPie = (estado: EstadoDeSesion = 'fuera', usuario: Usuario | null = null) =>
  render(
    <MemoryRouter>
      <Contexto.Provider value={{ estado, usuario, recargar: async () => {}, salir: async () => {} }}>
        <Pie />
      </Contexto.Provider>
    </MemoryRouter>,
  )

describe.each([
  ['Términos de uso', Terminos],
  ['Política de privacidad', Privacidad],
])('%s', (titulo, Pagina) => {
  it('tiene su titulo, su descripcion y se puede indexar', () => {
    enRuta(<Pagina />)
    expect(screen.getByRole('heading', { level: 1, name: titulo })).toBeInTheDocument()
    expect(document.title).toBe(`${titulo} · ZASS!`)
    expect(document.head.querySelector<HTMLMetaElement>('meta[name="description"]')!.content.length).toBeGreaterThan(40)
    expect(document.head.querySelector('meta[name="robots"]')).toBeNull()
  })

  it('muestra el aviso de borrador arriba, antes del titulo, mientras BORRADOR_LEGAL sea true', () => {
    const { container } = enRuta(<Pagina />)
    // Cuando Pablo cambie la constante a false, el aviso desaparece y esta prueba comprueba eso.
    expect(screen.queryByRole('note') !== null).toBe(BORRADOR_LEGAL)
    if (!BORRADOR_LEGAL) return
    const aviso = screen.getByRole('note')
    expect(aviso).toHaveTextContent('Borrador: pendiente de revisión legal')
    const h1 = container.querySelector('h1')!
    expect(aviso.compareDocumentPosition(h1) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('deja a la vista el dato que falta: responsable y correo de privacidad', () => {
    const { container } = enRuta(<Pagina />)
    expect(container).toHaveTextContent(RESPONSABLE.replace(/[[\]]/g, '') && 'por completar')
    expect(container.querySelectorAll('.legal-pendiente').length).toBeGreaterThan(0)
  })

  it('no promete lo que el producto no tiene', () => {
    const { container } = enRuta(<Pagina />)
    const texto = container.textContent ?? ''
    // «aún no existe» se puede decir; lo que no se puede es afirmar que existe.
    expect(texto).not.toMatch(/pago seguro|garantizamos|verificad[oa]s por ZASS|agenda tu cita|reserva en línea/i)
  })
})

describe('Términos', () => {
  it('dice que buscar es gratis y que el profesional paga una mensualidad', () => {
    enRuta(<Terminos />)
    expect(screen.getByText(/Buscar y contactar a alguien no te cuesta nada/)).toBeInTheDocument()
    expect(screen.getByText(/paga una mensualidad/)).toBeInTheDocument()
  })

  it('aclara que hoy no hay credenciales revisadas', () => {
    enRuta(<Terminos />)
    expect(screen.getByText(/todavía no revisa credenciales/)).toBeInTheDocument()
  })

  it('enlaza a la politica de privacidad', () => {
    enRuta(<Terminos />)
    expect(screen.getAllByRole('link', { name: /privacidad/i })[0]).toHaveAttribute('href', '/privacidad')
  })
})

describe('Privacidad', () => {
  it('lista los cinco derechos de la Ley 21.719', () => {
    enRuta(<Privacidad />)
    const seccion = screen.getByRole('heading', { name: /Tus derechos \(Ley 21\.719\)/ })
    expect(seccion).toBeInTheDocument()
    for (const derecho of ['Acceso', 'Rectificación', 'Supresión', 'Oposición', 'Portabilidad'])
      expect(screen.getByText(derecho, { selector: 'strong' })).toBeInTheDocument()
  })

  it('dice que la ubicacion al buscar no se guarda', () => {
    enRuta(<Privacidad />)
    expect(screen.getByText('No la guardamos')).toBeInTheDocument()
  })

  it('nombra las cookies de sesion, que son httpOnly, y la marca local', () => {
    enRuta(<Privacidad />)
    expect(screen.getByText('zass_sesion')).toBeInTheDocument()
    expect(screen.getByText('zass_refresco')).toBeInTheDocument()
    expect(screen.getByText('zass-sesion')).toBeInTheDocument()
    expect(screen.getByText(/httpOnly/)).toBeInTheDocument()
  })

  it('declara que no hay publicidad ni seguimiento de terceros, y avisa de Google Fonts', () => {
    enRuta(<Privacidad />)
    expect(screen.getByText(/No usamos publicidad ni herramientas de seguimiento/)).toBeInTheDocument()
    expect(screen.getByText(/Google Fonts/, { exact: false })).toBeInTheDocument()
  })

  it('dice como se guarda la contrasena: solo una huella irreversible', () => {
    enRuta(<Privacidad />)
    expect(screen.getByText(/huella cifrada irreversible/)).toBeInTheDocument()
  })

  it('explica que la direccion no se muestra', () => {
    enRuta(<Privacidad />)
    expect(screen.getByText(/La dirección se guarda cifrada/)).toBeInTheDocument()
  })
})

describe('AvisoDeBorrador', () => {
  it('no aparece cuando el borrador se da por revisado', () => {
    const { container } = render(<AvisoDeBorrador activo={false} />)
    expect(container).toBeEmptyDOMElement()
  })
})

describe('DatoLegal', () => {
  it('resalta lo pendiente y deja limpio lo lleno', () => {
    expect(estaPendiente(RESPONSABLE)).toBe(true)
    expect(estaPendiente(CORREO_PRIVACIDAD)).toBe(true)
    expect(estaPendiente('Ejemplo SpA, 76.123.456-7')).toBe(false)

    const { container, rerender } = render(<DatoLegal valor="[por completar]" />)
    expect(container.querySelector('.legal-pendiente')).toHaveTextContent('[por completar]')
    rerender(<DatoLegal valor="Ejemplo SpA" />)
    expect(container.querySelector('.legal-pendiente')).toBeNull()
    expect(container).toHaveTextContent('Ejemplo SpA')
  })

  it('un correo lleno se vuelve enlace; uno pendiente, no', () => {
    const { rerender } = render(<DatoLegal valor="privacidad@zass.cl" comoCorreo />)
    expect(screen.getByRole('link', { name: 'privacidad@zass.cl' })).toHaveAttribute('href', 'mailto:privacidad@zass.cl')
    rerender(<DatoLegal valor="[correo de privacidad por completar]" comoCorreo />)
    expect(screen.queryByRole('link')).toBeNull()
  })
})

describe('enlaces a las paginas legales', () => {
  it('el pie lleva a terminos y a privacidad', () => {
    elPie()
    const pie = within(screen.getByRole('contentinfo'))
    expect(pie.getByRole('link', { name: 'Términos de uso' })).toHaveAttribute('href', '/terminos')
    expect(pie.getByRole('link', { name: 'Privacidad' })).toHaveAttribute('href', '/privacidad')
  })

  // D5 del repaso en navegador: el pie le ofrecia «Entrar» y «Crear una cuenta» a quien ya estaba
  // dentro, en la misma pantalla en que la cabecera le ofrece salir.
  it('a quien esta fuera le ofrece entrar y crear una cuenta', () => {
    elPie('fuera')
    const pie = within(screen.getByRole('contentinfo'))
    expect(pie.getByRole('link', { name: 'Entrar' })).toHaveAttribute('href', '/entrar')
    expect(pie.getByRole('link', { name: 'Crear una cuenta' })).toHaveAttribute('href', '/crear-cuenta')
  })

  it('a quien ya entro no le ofrece entrar ni crear una cuenta: le ofrece sus propias paginas', () => {
    elPie('dentro', DENTRO)
    const pie = within(screen.getByRole('contentinfo'))
    expect(pie.queryByRole('link', { name: 'Entrar' })).toBeNull()
    expect(pie.queryByRole('link', { name: 'Crear una cuenta' })).toBeNull()
    expect(pie.getByRole('link', { name: 'Mis citas' })).toHaveAttribute('href', '/mis-citas')
    expect(pie.getByRole('link', { name: 'Ofrecer mis servicios' })).toHaveAttribute('href', '/ofrecer-servicios')
  })

  it('al prestador le ofrece su ficha, no «ofrecer mis servicios»', () => {
    elPie('dentro', { ...DENTRO, roles: ['usuario', 'prestador'] })
    const pie = within(screen.getByRole('contentinfo'))
    expect(pie.getByRole('link', { name: 'Mi ficha' })).toHaveAttribute('href', '/mi-ficha')
    expect(pie.queryByRole('link', { name: 'Ofrecer mis servicios' })).toBeNull()
  })

  it('mientras no se sabe si hay sesion no parpadea ninguna de las dos', () => {
    elPie('cargando')
    const pie = within(screen.getByRole('contentinfo'))
    expect(pie.queryByRole('link', { name: 'Entrar' })).toBeNull()
    expect(pie.queryByRole('link', { name: 'Mis citas' })).toBeNull()
  })

  it('el registro dice «Al crear tu cuenta aceptas» con enlaces a las dos, en pestaña nueva para no perder el formulario', () => {
    render(<Registrarse alRegistrarse={() => {}} />)
    expect(screen.getByText(/Al crear tu cuenta aceptas los/)).toBeInTheDocument()
    const terminos = screen.getByRole('link', { name: 'Términos de uso' })
    const privacidad = screen.getByRole('link', { name: 'Política de privacidad' })
    expect(terminos).toHaveAttribute('href', '/terminos')
    expect(privacidad).toHaveAttribute('href', '/privacidad')
    for (const a of [terminos, privacidad]) {
      expect(a).toHaveAttribute('target', '_blank')
      expect(a.getAttribute('rel')).toContain('noopener')
    }
  })
})
