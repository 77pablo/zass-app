import { useState } from 'react'
import { ErrorApi } from '../api/cliente'
import { ACEPTA_FOTO, TAMANO_MAXIMO, quitarFoto, subirFoto } from '../funciones/perfil/servicio'
import { Icono } from '../marco/Icono'

/**
 * JQ-9. Subir, cambiar o quitar la foto del perfil, en «Mi ficha». La foto se reduce y se limpia
 * en el servidor: aquí no se recorta ni se comprime nada, solo se avisa antes de mandar un archivo
 * que de todos modos el servidor iba a rechazar por pesado. (Textos PROPUESTOS: los aprueba Pablo.)
 */
export function FotoDePerfil({
  tieneFoto, alCambiar,
}: { tieneFoto: boolean; alCambiar: () => void }) {
  const [trabajando, setTrabajando] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function subir(archivo: File | undefined) {
    if (!archivo) return
    setError(null)
    if (archivo.size > TAMANO_MAXIMO) {
      setError('Esa foto pesa más de 10 MB. Prueba con otra.')
      return
    }
    setTrabajando(true)
    try {
      await subirFoto(archivo)
      alCambiar()
    } catch (e) {
      setError(e instanceof ErrorApi ? e.mensaje : 'No pudimos subir la foto. Intenta de nuevo.')
    } finally {
      setTrabajando(false)
    }
  }

  async function quitar() {
    setError(null)
    setTrabajando(true)
    try {
      await quitarFoto()
      alCambiar()
    } catch (e) {
      setError(e instanceof ErrorApi ? e.mensaje : 'No pudimos quitar la foto. Intenta de nuevo.')
    } finally {
      setTrabajando(false)
    }
  }

  return (
    <div className="foto-de-perfil">
      <label className={trabajando ? 'editar adjuntar adjuntar--ocupado' : 'editar adjuntar'}>
        <Icono nombre="mas" />
        {trabajando ? 'Subiendo…' : tieneFoto ? 'Cambiar foto' : 'Subir una foto'}
        <span className="solo-lector"> (JPG, PNG o WebP, hasta 10 MB)</span>
        <input
          type="file" className="solo-lector" accept={ACEPTA_FOTO} disabled={trabajando}
          onChange={(e) => { void subir(e.target.files?.[0]); e.target.value = '' }}
        />
      </label>
      {tieneFoto && (
        <button type="button" className="editar" disabled={trabajando} onClick={() => void quitar()}>
          Quitar foto
        </button>
      )}
      {error && <p className="campo-error" role="alert">{error}</p>}
    </div>
  )
}
