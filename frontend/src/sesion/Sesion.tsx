import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { ErrorApi } from '../api/cliente'
import { cerrarSesion, miCuenta } from '../funciones/identidad/servicio'
import { Contexto, type EstadoDeSesion, type Usuario } from './contexto'
import { hayMarcaDeSesion, ponerMarcaDeSesion, quitarMarcaDeSesion } from './marca'

export function ProveedorDeSesion({ children }: { children: ReactNode }) {
  const [estado, setEstado] = useState<EstadoDeSesion>(() => (hayMarcaDeSesion() ? 'cargando' : 'fuera'))
  const [usuario, setUsuario] = useState<Usuario | null>(null)

  const recargar = useCallback(async () => {
    try {
      setUsuario(await miCuenta<Usuario>())
      ponerMarcaDeSesion()
      setEstado('dentro')
    } catch (e) {
      // Un 401 es "no hay sesion", y es lo normal para quien nunca entro. Cualquier otro
      // fallo (sin red, 500) tambien deja a la persona fuera: es mejor mostrarle la
      // portada que una pantalla de error en la puerta.
      if (!(e instanceof ErrorApi)) throw e
      quitarMarcaDeSesion()
      setUsuario(null)
      setEstado('fuera')
    }
  }, [])

  useEffect(() => {
    // La carga inicial vive en el efecto con su propia bandera: si el proveedor se
    // desmonta antes de que responda la API, la respuesta no toca un estado que ya no existe.
    if (!hayMarcaDeSesion()) return
    let vigente = true
    miCuenta<Usuario>()
      .then((u) => { if (vigente) { setUsuario(u); setEstado('dentro') } })
      .catch(() => {
        if (!vigente) return
        quitarMarcaDeSesion()
        setUsuario(null)
        setEstado('fuera')
      })
    return () => { vigente = false }
  }, [])

  const salir = useCallback(async () => {
    try {
      await cerrarSesion()
    } catch {
      // Aunque la API falle se sale igual de la pantalla: dejarla como si siguiera dentro
      // es peor que cerrar de mas. (La cookie de refresco vive hasta vencer si el
      // servidor no recibio el cierre; sigue pendiente decidir si se le avisa a la persona.)
    } finally {
      quitarMarcaDeSesion()
      setUsuario(null)
      setEstado('fuera')
    }
  }, [])

  const valor = useMemo(
    () => ({ estado, usuario, recargar, salir }),
    [estado, usuario, recargar, salir],
  )
  return <Contexto.Provider value={valor}>{children}</Contexto.Provider>
}
