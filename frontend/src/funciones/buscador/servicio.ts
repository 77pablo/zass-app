import { enviar, obtener } from '../../api/cliente'

/**
 * API-23b. Lo que la persona dejo guardado para buscar (CU-04) y a quienes guardo (CS-08).
 * `favoritos` son solo identificadores: alcanzan para pintar el boton de guardar en la busqueda.
 */
export interface MiPerfilBuscador {
  zonaInteresId: number | null
  radioPreferidoKm: number
  aceptaAvisos: boolean
  canalCorreo: boolean
  canalPush: boolean
  rubrosInteres: number[]
  favoritos: string[]
}

/**
 * CS-08. Una persona guardada, con lo mismo que ya es publico en la busqueda. Lo que este tipo
 * NO tiene es a proposito: ni telefono, ni WhatsApp, ni correo. El contacto se ve en la ficha,
 * con cuenta verificada (RN-S1-09); la API no lo manda aqui y el tipo no tiene donde guardarlo.
 */
export interface Favorito {
  perfilId: string
  slug: string
  nombrePublico: string
  urlFoto: string | null
  rubro: string | null
  zonaPrincipal: string | null
  tieneCredencialVerificada: boolean
  /** Nulo con menos de 3 reseñas (RN-S5-11). */
  promedioCalificacion: number | null
  totalResenas: number
}

/** Lo que se manda a API-23. `rubrosInteres` ausente = no tocarlos. */
export interface Preferencias {
  zonaInteresId: number | null
  radioPreferidoKm: number
  aceptaAvisos: boolean
  canalCorreo: boolean
  canalPush: boolean
  rubrosInteres?: number[]
}

/** CU-04: hasta cinco oficios. El mismo tope que aplica el servidor. */
export const MAX_RUBROS_DE_INTERES = 10

export function miPerfilBuscador(): Promise<MiPerfilBuscador> {
  return obtener<MiPerfilBuscador>('/api/mi/perfil-buscador')
}

export function guardarPreferencias(p: Preferencias): Promise<void> {
  return enviar<void>('/api/mi/perfil-buscador', p, 'PUT')
}

export function misFavoritos(): Promise<Favorito[]> {
  return obtener<Favorito[]>('/api/mi/favoritos')
}

export function guardarFavorito(perfilId: string): Promise<void> {
  return enviar<void>(`/api/mi/favoritos/${encodeURIComponent(perfilId)}`, undefined, 'PUT')
}

export function quitarFavorito(perfilId: string): Promise<void> {
  return enviar<void>(`/api/mi/favoritos/${encodeURIComponent(perfilId)}`, undefined, 'DELETE')
}
