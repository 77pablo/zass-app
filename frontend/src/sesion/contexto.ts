import { createContext, useContext } from 'react'

/** API-13. Lo que la API sabe de quien esta dentro. */
export interface Usuario {
  id: string
  correo: string
  nombre: string
  correoVerificado: boolean
  roles: string[]
}

/**
 * 'cargando' existe a proposito: al abrir la pagina todavia no se sabe si hay sesion, y
 * mostrar "Entrar" un segundo para despues cambiarlo a "Mi ficha" es un parpadeo que le
 * dice a la persona que algo se rompio.
 */
export type EstadoDeSesion = 'cargando' | 'fuera' | 'dentro'

interface ContextoDeSesion {
  estado: EstadoDeSesion
  usuario: Usuario | null
  /** Vuelve a preguntar a la API. Se llama despues de entrar o de crear la cuenta. */
  recargar: () => Promise<void>
  salir: () => Promise<void>
}

export const Contexto = createContext<ContextoDeSesion | null>(null)

export function useSesion(): ContextoDeSesion {
  const c = useContext(Contexto)
  if (c === null) throw new Error('useSesion se usa dentro de <ProveedorDeSesion>.')
  return c
}
