import type { ReactNode } from 'react'
import { ContextoDelMenuMovil, useEstadoDelMenu } from './menuMovil'

/** Comparte el estado del menu del telefono entre la cabecera y la barra inferior (menuMovil.ts). */
export function ProveedorDeMenuMovil({ children }: { children: ReactNode }) {
  const menu = useEstadoDelMenu()
  return <ContextoDelMenuMovil.Provider value={menu}>{children}</ContextoDelMenuMovil.Provider>
}
