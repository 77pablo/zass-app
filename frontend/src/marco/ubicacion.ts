export interface PuntoDePersona {
  lat: number
  lon: number
}

/**
 * La posicion de la persona vive SOLO en memoria, nunca en la URL ni en el almacenamiento del navegador. Una URL se
 * comparte, se guarda en el historial y se manda por WhatsApp: ahi la ubicacion de alguien iria a parar a quien no
 * la pidio. En memoria sobrevive a ir a una ficha y volver, y se pierde al recargar.
 */
let guardada: PuntoDePersona | null = null

export const ubicacionGuardada = (): PuntoDePersona | null => guardada
export const guardarUbicacion = (p: PuntoDePersona): void => { guardada = p }
export const olvidarUbicacion = (): void => { guardada = null }
