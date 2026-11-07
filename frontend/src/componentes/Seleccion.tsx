export interface OpcionDeSeleccion {
  valor: string
  texto: string
  /** Si alguna opcion trae grupo, la lista se muestra agrupada (optgroup). */
  grupo?: string
}

/**
 * Lista desplegable. Mismo contrato de accesibilidad que Campo: la etiqueta va unida
 * por htmlFor/id y el error se anuncia con aria-describedby.
 *
 * La primera opcion (`vacio`) es la que dice "Elige...": sin ella, el navegador
 * preselecciona la primera de verdad y la persona busca por algo que no eligio. Se
 * omite cuando SIEMPRE hay un valor valido, como un radio que parte en 10 km.
 */
export function Seleccion({
  id, etiqueta, valor, alCambiar, opciones, vacio, error, ayuda,
}: {
  id: string
  etiqueta: string
  valor: string
  alCambiar: (v: string) => void
  opciones: OpcionDeSeleccion[]
  vacio?: string
  error?: string
  ayuda?: string
}) {
  const idError = `${id}-error`
  const idAyuda = `${id}-ayuda`
  const mostrarAyuda = ayuda !== undefined && error === undefined
  const descrito = error ? idError : mostrarAyuda ? idAyuda : undefined

  const sinGrupo = opciones.filter((o) => o.grupo === undefined)
  const grupos = [...new Set(opciones.flatMap((o) => (o.grupo === undefined ? [] : [o.grupo])))]
    .sort((a, b) => a.localeCompare(b, 'es'))

  return (
    <div className="campo">
      <label className="campo-etiqueta" htmlFor={id}>{etiqueta}</label>
      <select
        className={error ? 'campo-entrada campo-entrada--mal' : 'campo-entrada'}
        id={id}
        name={id}
        value={valor}
        aria-invalid={error ? true : undefined}
        aria-describedby={descrito}
        onChange={(e) => alCambiar(e.target.value)}
      >
        {vacio !== undefined && <option value="">{vacio}</option>}
        {sinGrupo.map((o) => <option key={o.valor} value={o.valor}>{o.texto}</option>)}
        {grupos.map((g) => (
          <optgroup key={g} label={g}>
            {opciones.filter((o) => o.grupo === g).map((o) => (
              <option key={o.valor} value={o.valor}>{o.texto}</option>
            ))}
          </optgroup>
        ))}
      </select>
      {mostrarAyuda && <p className="campo-ayuda" id={idAyuda}>{ayuda}</p>}
      {error && <p className="campo-error" id={idError}>{error}</p>}
    </div>
  )
}
