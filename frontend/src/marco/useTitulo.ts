import { useEffect } from 'react'

/**
 * El titulo de la pestana cambia con la pagina. En un sitio de varias paginas es lo que
 * aparece en el historial, en los favoritos y en lo que lee un lector de pantalla al llegar.
 */
export function useTitulo(titulo: string | null): void {
  useEffect(() => {
    if (titulo === null) return
    document.title = titulo === '' ? 'ZASS! · Conecta con un toque' : `${titulo} · ZASS!`
  }, [titulo])
}
