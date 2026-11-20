import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { olvidarCsrf } from '../api/cliente'
import type { Suscripcion } from '../funciones/perfil/servicio'
import { TarjetaDelFundador, debePedirTarjeta } from './TarjetaDelFundador'

/*
 * Cupo sin tarjeta (decisión de Pablo, 20-nov-2026): la tarjeta se inscribe durante el mes
 * gratis, desde Mi ficha. Se vigila a quién se le pide y que el botón vaya a API-76, no a
 * API-69 (que tomaría el cupo, no inscribiría la tarjeta).
 */

function json(cuerpo: unknown, estado = 200) {
  return new Response(JSON.stringify(cuerpo), {
    status: estado, headers: { 'content-type': 'application/json' },
  })
}

const FUNDADOR: Suscripcion = {
  plan: 'ZASS! Fundador', estado: 'Prueba', vigente: true, enPrueba: true,
  vigenteHasta: '2026-12-20T15:00:00Z', diasRestantes: 30, renovacionAutomatica: false,
  esFundador: true, tarjetaInscrita: false,
}

let llamadas: { ruta: string, clave: string | null }[]
let respuesta: () => Response

beforeEach(() => {
  llamadas = []
  respuesta = () => json({
    id: 'i1', codigoPlan: 'fundador', precioMensual: 0, descuento: 0, total: 0, cupon: null,
    estado: 'Pendiente', urlParaPagar: '/pago/inscripcion?intento=i1', referenciaPasarela: null,
    nueva: true,
  })
  olvidarCsrf()
  vi.stubGlobal('fetch', vi.fn(async (entrada: RequestInfo | URL, opciones?: RequestInit) => {
    const ruta = String(entrada)
    if (ruta.includes('/api/auth/csrf')) return json({ token: 't' })
    llamadas.push({ ruta, clave: new Headers(opciones?.headers).get('Idempotency-Key') })
    return respuesta()
  }))
})

function pintar(s: Suscripcion = FUNDADOR) {
  return render(
    <MemoryRouter initialEntries={['/mi-ficha']}>
      <Routes>
        <Route path="/mi-ficha" element={<TarjetaDelFundador suscripcion={s} />} />
        <Route path="/pago/inscripcion" element={<p>la pasarela</p>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('debePedirTarjeta', () => {
  it('solo al fundador vigente sin tarjeta', () => {
    expect(debePedirTarjeta(FUNDADOR)).toBe(true)
    expect(debePedirTarjeta({ ...FUNDADOR, tarjetaInscrita: true })).toBe(false)
    expect(debePedirTarjeta({ ...FUNDADOR, esFundador: false })).toBe(false)
    expect(debePedirTarjeta({ ...FUNDADOR, vigente: false })).toBe(false)
    // Una API vieja no manda los campos nuevos: no se le pide nada a nadie.
    const { esFundador: _a, tarjetaInscrita: _b, ...vieja } = FUNDADOR
    expect(debePedirTarjeta(vieja)).toBe(false)
    expect(debePedirTarjeta(null)).toBe(false)
  })
})

describe('TarjetaDelFundador', () => {
  it('dice hasta cuándo y qué pasa si no la inscribe', () => {
    pintar()

    expect(screen.getByText('20 de diciembre de 2026')).toBeInTheDocument()
    expect(screen.getByText(/hoy no se te cobra nada/)).toBeInTheDocument()
    expect(screen.getByText(/tu cupo queda libre para otra persona/)).toBeInTheDocument()
  })

  it('el botón inscribe la tarjeta con API-76 y lleva a la pasarela', async () => {
    pintar()

    await userEvent.click(screen.getByRole('button', { name: 'Inscribir mi tarjeta' }))

    expect(await screen.findByText('la pasarela')).toBeInTheDocument()
    expect(llamadas).toHaveLength(1)
    expect(llamadas[0].ruta).toContain('/api/mi/suscripcion/tarjeta')
    expect(llamadas[0].clave).not.toBeNull()
  })

  it('si el servidor dice que no, muestra su motivo', async () => {
    respuesta = () => json({ title: 'Conflicto', detail: 'Primero toma tu cupo de fundador.' }, 409)
    pintar()

    await userEvent.click(screen.getByRole('button', { name: 'Inscribir mi tarjeta' }))

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent('Primero toma tu cupo de fundador.')
    })
  })
})
