import type { CSSProperties } from 'react'

/*
 * Esqueletos: bloques con la forma de lo que va a llegar, en vez de «Cargando…».
 *
 * Por que: un «Cargando…» solo dice «espera»; un esqueleto dice QUE viene y DONDE, y como mide
 * lo mismo que el contenido, la pantalla no salta cuando llega. El brillo es una respiracion
 * suave (opacidad), no un barrido con degradado: la guia v3 prohibe los degradados. Con
 * «reducir movimiento» se queda quieto (regla global en index.css).
 *
 * Son SOLO para la vista (aria-hidden). Lo que oye un lector de pantalla lo pone quien los usa,
 * con un `role="status"` y su texto («Buscando…», «Cargando tus citas…»).
 */

/** Un bloque gris. `ancho` y `alto` en cualquier unidad CSS. */
export function Hueso({ ancho = '100%', alto = '1em', forma = 'linea' }: {
  ancho?: string; alto?: string; forma?: 'linea' | 'circulo' | 'pildora'
}) {
  const estilo: CSSProperties = { width: ancho, height: alto }
  return <span className={`esqueleto esqueleto--${forma}`} style={estilo} aria-hidden="true" />
}

/**
 * La tarjeta de una persona mientras se busca. Usa las MISMAS clases que TarjetaPersona
 * (tarjeta-cabeza, fila-etiquetas, tarjeta-ver): asi mide lo mismo y no hay salto al llegar.
 */
export function TarjetaPersonaEsqueleto() {
  return (
    <div className="tarjeta-persona tarjeta-persona--esqueleto" aria-hidden="true">
      <div className="tarjeta-cabeza">
        <Hueso forma="circulo" ancho="60px" alto="60px" />
        <div className="esqueleto-columna">
          <Hueso ancho="9.5rem" alto="1.35rem" />
          <Hueso ancho="7rem" alto="0.95rem" />
        </div>
      </div>
      {/* Tres etiquetas, como la tarjeta tipica (verificado, zona y reputacion): en un telefono
          bajan a una segunda fila, igual que las de verdad. Medido el 18-nov: 243 px las dos. */}
      <div className="fila-etiquetas">
        <Hueso forma="pildora" ancho="6.4rem" alto="1.95rem" />
        <Hueso forma="pildora" ancho="5.6rem" alto="1.95rem" />
        <Hueso forma="pildora" ancho="9.6rem" alto="1.95rem" />
      </div>
      <span className="tarjeta-ver">
        <Hueso ancho="6.5rem" alto="1.7rem" />
        <Hueso ancho="5rem" alto="1.1rem" />
      </span>
    </div>
  )
}

/**
 * Los resultados de una busqueda: el recuento de arriba («corredora · 3 personas · …», que en un
 * telefono ocupa dos lineas) y las tarjetas, en la misma rejilla y con el mismo espacio entre
 * ellos (`.resultados-cuenta + .tarjetas-rejilla`). Asi, al llegar, las tarjetas caen donde ya
 * estaban sus esqueletos.
 */
export function ResultadosEsqueleto({ cuantas = 3 }: { cuantas?: number }) {
  return (
    <>
      <div className="resultados-cuenta resultados-cuenta--esqueleto" aria-hidden="true">
        <Hueso ancho="min(100%, 22rem)" alto="1.2rem" />
        <Hueso ancho="9rem" alto="1.2rem" />
      </div>
      <ul className="tarjetas-rejilla tarjetas-rejilla--esqueleto" aria-hidden="true">
        {Array.from({ length: cuantas }, (_, i) => <li key={i}><TarjetaPersonaEsqueleto /></li>)}
      </ul>
    </>
  )
}

/**
 * Para las pantallas del panel (mis citas, mi agenda, mi ficha…): bloques con forma de
 * tarjeta de lista. No sabe que pantalla es, asi que es una forma general: un titulo de
 * seccion y tres tarjetas de dos lineas.
 */
export function ListaEsqueleto({ filas = 3 }: { filas?: number }) {
  return (
    <div className="lista-esqueleto" aria-hidden="true">
      <Hueso ancho="11rem" alto="1.6rem" />
      {Array.from({ length: filas }, (_, i) => (
        <div key={i} className="lista-esqueleto-fila">
          <Hueso ancho="65%" alto="1.1rem" />
          <Hueso ancho="40%" alto="0.95rem" />
        </div>
      ))}
    </div>
  )
}
