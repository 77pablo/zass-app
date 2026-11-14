/*
 * La pantalla mientras se carga, CON su título.
 *
 * Por qué existe: hasta el 14-nov-2026 las pantallas del panel devolvían solo una línea
 * «Cargando…» y la página no tenía NINGÚN `h1` en ese rato (medido en un navegador real:
 * `[]` al llegar a `/mi-ficha`, el nombre tres segundos después). Para quien navega con
 * lector de pantalla, eso es llegar a una página que no dice dónde está; y la primera cosa
 * que se busca al aterrizar es el encabezado.
 *
 * El título es el mismo que la pantalla ya le da a la pestaña con `useTitulo`, para que al
 * terminar de cargar no cambie de nombre delante de quien lo está leyendo.
 */
export function PaginaCargando({ titulo, aviso = 'Cargando…' }: {
  /** El mismo que usa `useTitulo` en esa pantalla. */
  titulo: string
  /** Qué se está cargando, si vale la pena decirlo. */
  aviso?: string
}) {
  return (
    <div className="contenedor ficha-pagina">
      <h1 className="pagina-titulo">{titulo}</h1>
      <p className="cargando-linea" role="status">{aviso}</p>
    </div>
  )
}
