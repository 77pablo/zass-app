import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { PaginaCargando } from './PaginaCargando'

/*
 * Lo que vigila este archivo es el componente. Que ninguna pantalla vuelva al patrón viejo
 * lo vigila `pruebas-functions/encabezado-al-cargar.prueba.js`, que lee todas las pantallas
 * (aquí no se puede: el tsconfig del frontend no trae los tipos de Node).
 *
 * Y lo que jsdom no puede hacer es medir cuánto dura ese rato de carga; eso se vio en un
 * navegador real: al llegar a `/mi-ficha` no había NINGÚN `h1` y el nombre aparecía tres
 * segundos después.
 */
describe('La pantalla mientras carga', () => {
  it('tiene un h1 con el título de la pantalla y avisa a los lectores', () => {
    render(<PaginaCargando titulo="Mi ficha" />)

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Mi ficha')
    expect(screen.getByRole('status')).toHaveTextContent('Cargando…')
  })

  it('el aviso se puede concretar sin perder el título', () => {
    render(<PaginaCargando titulo="Mis citas" aviso="Cargando tus citas…" />)

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Mis citas')
    expect(screen.getByRole('status')).toHaveTextContent('Cargando tus citas…')
  })
})
