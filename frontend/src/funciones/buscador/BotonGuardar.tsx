import { Link } from 'react-router-dom'
import { Icono } from '../../marco/Icono'
import { useSesion } from '../../sesion/contexto'
import type { Favoritos } from './useFavoritos'

/**
 * CS-08. El boton para guardar a una persona. Es un `chip` con `aria-pressed`, el mismo control
 * de dos estados que los filtros de la busqueda: quien lo usa ya sabe leerlo.
 *
 * El icono es la estrella: el marco todavia no tiene corazon (ver el PR, nota para Joaquin).
 *
 * Sin sesion no es un boton que falla: es un enlace a crear la cuenta, que vuelve a `despues`
 * igual que el contacto de la ficha. La parte «necesitas una cuenta» va para el lector de
 * pantalla; a la vista, la palabra es la misma que con sesion, para que la tarjeta no cambie.
 * Sin el nombre de la persona: en la lista de resultados es un enlace mas al lado del de la
 * tarjeta, y dos enlaces con el mismo nombre confunden a quien recorre los enlaces de la pagina.
 * El `li` que los contiene ya dice de quien es. Con sesion si lleva el nombre: es un boton.
 * El espacio va FUERA del texto oculto: el calculo del nombre accesible se come el espacio con
 * que empieza un elemento en linea, y quedaba «Guardara Marcela» (lo vio la prueba).
 */
export function BotonGuardar({ perfilId, nombre, despues, favoritos }: {
  perfilId: string
  nombre: string
  /** A donde volver despues de crear la cuenta. */
  despues: string
  favoritos: Favoritos
}) {
  const { estado } = useSesion()

  if (estado === 'fuera') {
    return (
      <Link className="chip" to="/crear-cuenta" state={{ despues }}>
        <Icono nombre="estrella" />Guardar{' '}
        <span className="solo-lector">(crea tu cuenta gratis)</span>
      </Link>
    )
  }

  const guardado = favoritos.guardado(perfilId)
  // Mientras no se sabe si esta guardado no se muestra nada: un «Guardar» que un segundo despues
  // pasa a «Guardado» le diria a la persona que algo cambio solo.
  if (estado === 'cargando' || guardado === null) return null

  return (
    <button
      type="button" className="chip" aria-pressed={guardado}
      disabled={favoritos.enCurso(perfilId)}
      onClick={() => void favoritos.alternar(perfilId)}
    >
      <Icono nombre="estrella" />{guardado ? 'Guardado' : 'Guardar'}{' '}
      <span className="solo-lector">a {nombre}</span>
    </button>
  )
}
