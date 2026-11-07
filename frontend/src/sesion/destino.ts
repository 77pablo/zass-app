/** A donde volver tras entrar: lo que la persona queria abrir cuando se le pidio entrar. */
export function destinoDe(estado: unknown): string | null {
  const despues = (estado as { despues?: unknown } | null)?.despues
  return typeof despues === 'string' && despues.startsWith('/') && !despues.startsWith('//')
    ? despues : null
}
