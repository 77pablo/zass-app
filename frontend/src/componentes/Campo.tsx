/**
 * Campo de formulario. La etiqueta va unida al input con htmlFor/id, y el error
 * se anuncia con aria-describedby: sin eso, quien usa lector de pantalla oye
 * "campo de texto" y nunca se entera de que esta mal.
 */
export function Campo({
  id, etiqueta, valor, alCambiar, tipo = 'text', error, autoComplete, ayuda, maximo,
}: {
  id: string
  etiqueta: string
  valor: string
  alCambiar: (v: string) => void
  tipo?: string
  error?: string
  autoComplete?: string
  ayuda?: string
  maximo?: number
}) {
  const idError = `${id}-error`
  const idAyuda = `${id}-ayuda`
  // La ayuda se esconde cuando hay error. Si no, el campo repite la misma regla
  // dos veces ("Al menos 10 caracteres" y "Usa al menos 10 caracteres") y la
  // persona tiene que leer las dos para darse cuenta de que son lo mismo.
  const mostrarAyuda = ayuda !== undefined && error === undefined
  const descrito = (error ? idError : mostrarAyuda ? idAyuda : undefined)

  return (
    <div className="campo">
      <label className="campo-etiqueta" htmlFor={id}>{etiqueta}</label>
      <input
        className={error ? 'campo-entrada campo-entrada--mal' : 'campo-entrada'}
        id={id}
        name={id}
        type={tipo}
        value={valor}
        maxLength={maximo}
        autoComplete={autoComplete}
        aria-invalid={error ? true : undefined}
        aria-describedby={descrito}
        onChange={(e) => alCambiar(e.target.value)}
      />
      {mostrarAyuda && <p className="campo-ayuda" id={idAyuda}>{ayuda}</p>}
      {error && <p className="campo-error" id={idError}>{error}</p>}
    </div>
  )
}
