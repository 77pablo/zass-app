import { enviar, obtener } from '../../api/cliente'

/** Los enums viajan como texto (JsonStringEnumConverter), no como numero. */
export type EstadoPerfil = 'Borrador' | 'Publicado' | 'Despublicado'

/**
 * API-18. El perfil de quien esta dentro, como lo ve su dueno: con la zona principal y SIN
 * la ubicacion exacta (RN-S3-02: el tipo no tiene campos de coordenadas a proposito).
 */
export interface MiPerfil {
  id: string
  slug: string
  nombrePublico: string
  descripcion: string | null
  rubroId: number | null
  estado: EstadoPerfil
  zonaPrincipal: string | null
  tieneDireccion: boolean
  telefonoPublico: string | null
  correoPublico: string | null
  whatsapp: string | null
  sitioWeb: string | null
  totalResenas: number
  /** Nulo con menos de 3 resenas (RN-S5-11). */
  promedioCalificacion: number | null
  /** Token de concurrencia (CC-02): se devuelve tal cual al guardar. */
  version: number
  publicadoEn: string | null
  /** Lo que falta para publicar, en palabras: "el rubro", "al menos un servicio"... */
  faltaParaPublicar: string[]
}

/** Lo que se edita. Los vacios se mandan como nulos: "" se guardaria como un dato de cero caracteres. */
export interface DatosDelPerfil {
  nombrePublico: string
  descripcion: string
  rubroId: number | null
  telefonoPublico: string
  correoPublico: string
  whatsapp: string
  sitioWeb: string
}

/** API-20, lo que devuelve el GET. Incluye los desactivados: son filas que se conservan. */
export interface ServicioOfrecido {
  id: string
  servicioId: number
  nombre: string
  esCatalogo: boolean
  duracionMinutos: number
  modoAgenda: 'Automatico' | 'Manual'
  precioReferencia: number | null
  activo: boolean
}

/** API-20, lo que se manda: o se elige del catalogo (servicioId) o se escribe uno nuevo. */
export interface ServicioParaGuardar {
  servicioId?: number
  nombreNuevo?: string
  duracionMinutos: number
  modoAgenda: 'automatico' | 'manual'
  precioReferencia?: number | null
}

/** API-21. */
export interface ZonaCubierta {
  zonaId: number
  nombre: string
  sector: string
  esPrincipal: boolean
  desdeEn: string
}

export interface Suscripcion {
  plan: string
  estado: string
  vigente: boolean
  enPrueba: boolean
  vigenteHasta: string | null
  diasRestantes: number
  renovacionAutomatica: boolean
}

const PERFIL = '/api/mi/perfil-prestador'

export const miPerfil = () => obtener<MiPerfil>(PERFIL)
export const misServicios = () => obtener<ServicioOfrecido[]>(`${PERFIL}/servicios`)
export const miCobertura = () => obtener<ZonaCubierta[]>(`${PERFIL}/cobertura`)
export const miSuscripcion = () => obtener<Suscripcion>('/api/mi/suscripcion')

const nulo = (texto: string): string | null => (texto.trim() === '' ? null : texto.trim())

/**
 * API-19. `direccion`, `latitud` y `longitud` viajan en nulo a proposito: es lo que le dice
 * a la API "no toques la ubicacion". El formulario no la edita (el geocodificador no existe
 * todavia), y si el campo faltara, guardar el nombre podria borrar lo que otra pantalla
 * ponga mas adelante.
 */
export function guardarDatos(datos: DatosDelPerfil, version: number): Promise<void> {
  return enviar<void>(PERFIL, {
    nombrePublico: datos.nombrePublico.trim(),
    descripcion: nulo(datos.descripcion),
    rubroId: datos.rubroId,
    telefonoPublico: nulo(datos.telefonoPublico),
    correoPublico: nulo(datos.correoPublico),
    whatsapp: nulo(datos.whatsapp),
    sitioWeb: nulo(datos.sitioWeb),
    direccion: null,
    latitud: null,
    longitud: null,
    version,
  }, 'PUT')
}

/** API-20. Reemplaza la lista completa de lo que se ofrece, no un parche. */
export function guardarServicios(servicios: ServicioParaGuardar[]): Promise<void> {
  return enviar<void>(`${PERFIL}/servicios`, { servicios }, 'PUT')
}

/** API-21. Reemplaza la lista completa de zonas, con exactamente una principal. */
export function guardarCobertura(
  zonas: { zonaId: number; esPrincipal: boolean }[],
): Promise<void> {
  return enviar<void>(`${PERFIL}/cobertura`, { zonas }, 'PUT')
}

/** API-22. Publica y arranca la suscripcion de prueba. */
export const publicar = () => enviar<void>(`${PERFIL}/publicar`)
export const despublicar = () => enviar<void>(`${PERFIL}/despublicar`)
