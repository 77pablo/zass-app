import { useEffect, useState } from 'react'
import { Icono } from './Icono'

/**
 * «Compartir», como en una app: en el telefono abre el menu del sistema (WhatsApp, mensajes,
 * correo…). Donde no existe (casi todo escritorio), copia el enlace y lo dice.
 *
 * Si la persona cierra el menu sin elegir, el navegador lanza un AbortError: no es un error y
 * no se muestra nada.
 */
export function BotonCompartir({ titulo, texto, url }: {
  titulo: string
  texto: string
  /** Por omision, la direccion de la pagina en la que se esta. */
  url?: string
}) {
  const [estado, setEstado] = useState<'listo' | 'copiado' | 'fallo'>('listo')

  // El «Enlace copiado» se va solo: si se quedara, el boton diria algo que ya no es cierto.
  useEffect(() => {
    if (estado === 'listo') return
    const t = setTimeout(() => setEstado('listo'), 3000)
    return () => clearTimeout(t)
  }, [estado])

  async function compartir() {
    const enlace = url ?? window.location.href
    if (typeof navigator.share === 'function') {
      try {
        await navigator.share({ title: titulo, text: texto, url: enlace })
      } catch (e) {
        if (!(e instanceof DOMException && e.name === 'AbortError')) setEstado('fallo')
      }
      return
    }
    try {
      await navigator.clipboard.writeText(enlace)
      setEstado('copiado')
    } catch {
      setEstado('fallo')
    }
  }

  return (
    <>
      <button type="button" className="boton boton--chico boton-compartir" onClick={() => void compartir()}>
        <Icono nombre={estado === 'copiado' ? 'check' : 'compartir'} />
        {estado === 'copiado' ? 'Enlace copiado' : 'Compartir'}
      </button>
      <span className="solo-lector" role="status">
        {estado === 'copiado' ? 'Enlace copiado' : estado === 'fallo' ? 'No se pudo compartir. Copia la dirección de la página.' : ''}
      </span>
    </>
  )
}
