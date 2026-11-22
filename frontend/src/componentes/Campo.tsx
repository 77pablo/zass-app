/**
 * Campo de formulario. La etiqueta va unida al input con htmlFor/id, y el error
 * se anuncia con aria-describedby: sin eso, quien usa lector de pantalla oye
 * "campo de texto" y nunca se entera de que esta mal.
 *
 * Correos, telefonos y direcciones web (por `tipo` o por `teclado`) van sin corrector
 * ortografico y sin mayuscula automatica: el corrector «arreglaba» un correo y la mayuscula
 * inicial lo dejaba distinto del que la persona tiene (guias de interfaz, 18-nov-2026).
 */
const SIN_CORRECTOR = new Set(['email', 'tel', 'url'])

export function Campo({
  id, etiqueta, valor, alCambiar, tipo = 'text', error, autoComplete, ayuda, maximo, teclado,
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
  /**
   * El teclado del telefono (`inputMode`), sin cambiar el `type`: un `type="email"` o `"url"`
   * hace que el navegador valide por su cuenta y bloquee el envio con su propio mensaje.
   */
  teclado?: 'tel' | 'email' | 'url' | 'numeric' | 'decimal'
}) {
  const idError = `${id}-error`
  const idAyuda = `${id}-ayuda`
  // La ayuda se esconde cuando hay error. Si no, el campo repite la misma regla
  // dos veces ("Al menos 10 caracteres" y "Usa al menos 10 caracteres") y la
  // persona tiene que leer las dos para darse cuenta de que son lo mismo.
  const mostrarAyuda = ayuda !== undefined && error === undefined
  const descrito = (error ? idError : mostrarAyuda ? idAyuda : undefined)
  const sinCorrector = SIN_CORRECTOR.has(tipo) || (teclado !== undefined && SIN_CORRECTOR.has(teclado))

  return (
    <div className="campo">
      <label className="campo-etiqueta" htmlFor={id}>{etiqueta}</label>
      <input
        className={error ? 'campo-entrada campo-entrada--mal' : 'campo-entrada'}
        id={id}
        name={id}
        type={tipo}
        inputMode={teclado}
        value={valor}
        maxLength={maximo}
        autoComplete={autoComplete}
        spellCheck={sinCorrector ? false : undefined}
        autoCapitalize={sinCorrector ? 'none' : undefined}
        aria-invalid={error ? true : undefined}
        aria-describedby={descrito}
        onChange={(e) => alCambiar(e.target.value)}
      />
      {mostrarAyuda && <p className="campo-ayuda" id={idAyuda}>{ayuda}</p>}
      {error && <p className="campo-error" id={idError}>{error}</p>}
    </div>
  )
}
