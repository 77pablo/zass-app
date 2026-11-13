import { enviar, obtener, ErrorApi } from '../../api/cliente'

/**
 * MOD-11 · S5 (API-46 a API-51). Lo que la pantalla necesita saber de las reseñas y nada mas:
 * los endpoints internos del moderador (API-52 y API-53) son otra pantalla.
 *
 * Los enums viajan como TEXTO (JsonStringEnumConverter), igual que en la agenda.
 */

// ---------------------------------------------------------------- reglas que la pantalla repite

/**
 * RN-S5-03. Los mismos numeros que `ReglasDeResenas` en el servidor. Se repiten aqui para poder
 * avisar antes de enviar; el servidor los revalida igual, porque esto es una ayuda, no un control.
 */
export const CALIFICACION_MINIMA = 1
export const CALIFICACION_MAXIMA = 5
export const COMENTARIO_MINIMO = 20
export const COMENTARIO_MAXIMO = 1500
export const RESPUESTA_MAXIMA = 1000
export const DETALLE_MAXIMO = 1000

/**
 * El servidor cuenta el largo como PostgreSQL (`char_length`: puntos de codigo), no como
 * `string.Length`. En JavaScript `texto.length` cuenta unidades UTF-16, asi que con un emoji
 * diria 2 donde el servidor dice 1 y el contador de la pantalla mentiria justo en el borde.
 */
export const largo = (texto: string): number => [...texto].length

/**
 * Lo que significa cada nota. Va junto al selector porque un "4" suelto no le dice nada a nadie,
 * y dos personas que entienden cosas distintas por un 4 dejan reseñas que no se pueden comparar.
 */
const QUE_SIGNIFICA: Record<number, string> = {
  1: 'Muy malo',
  2: 'Malo',
  3: 'Aceptable',
  4: 'Bueno',
  5: 'Excelente',
}

export const significado = (valor: number): string => QUE_SIGNIFICA[valor] ?? ''

/** CU-16 paso 6. Lista cerrada, la misma de `ReglasDeResenas.AspectosPermitidos`. */
export const ASPECTOS = [
  { valor: 'puntualidad', etiqueta: 'Puntualidad' },
  { valor: 'claridad', etiqueta: 'Claridad al explicar' },
  { valor: 'precio_acordado', etiqueta: 'Respetó el precio acordado' },
] as const

/** RN-S5-07. Motivos tipificados; el texto libre es solo el detalle. */
export const MOTIVOS_DE_REPORTE = [
  { valor: 'DatosPersonales', etiqueta: 'Trae datos personales' },
  { valor: 'LenguajeDeOdio', etiqueta: 'Lenguaje de odio' },
  { valor: 'Falsedad', etiqueta: 'Es falsa' },
  { valor: 'ConflictoDeInteres', etiqueta: 'Conflicto de interés' },
  { valor: 'FueraDeTema', etiqueta: 'Fuera de tema' },
] as const

export type MotivoDeReporte = (typeof MOTIVOS_DE_REPORTE)[number]['valor']

// ---------------------------------------------------------------- tipos

export type OrigenContacto = 'Ficha' | 'Tarjeta' | 'Busqueda' | 'Cita'
export type CanalContacto = 'Telefono' | 'Correo' | 'Whatsapp' | 'Formulario'

export type MotivoDeNoElegibilidad =
  | 'Ninguno' | 'Autoresena' | 'SinContacto' | 'VentanaDeContacto'
  | 'TopeDe90Dias' | 'CitaSinRealizar' | 'CitaNoAsistio' | 'CuentaNoHabilitada'

/** API-47. `puedeDesde` es la fecha exacta desde la que se cumpliria lo que falta (CU-16 4B.2). */
export interface Elegibilidad {
  elegible: boolean
  contactoId: string | null
  motivo: MotivoDeNoElegibilidad
  mensaje: string | null
  puedeDesde: string | null
}

export type TipoResena = 'Servicio' | 'Inasistencia'
export type EstadoResena = 'Publicada' | 'OcultaEnModeracion' | 'Retirada'

/**
 * De donde salio el derecho a reseñar, y lo UNICO del contacto que sale hacia fuera (CU-16 §7).
 * `Directo` es la decision de Pablo del 11-nov-2026: quien marco "ya me atendio" deja una reseña
 * que ZASS! no vio pasar por su agenda, y eso se rotula.
 */
export type OrigenDeResena = 'Cita' | 'Directo'

/** Lo que ve su AUTOR: con el plazo de edicion y el estado. */
export interface Resena {
  id: string
  perfilPrestadorId: string
  calificacion: number
  comentario: string
  aspectos: string[]
  tipo: TipoResena
  estado: EstadoResena
  editada: boolean
  publicadaEn: string
  editableHasta: string
}

export interface RespuestaPublica {
  texto: string
  publicadaEn: string
  editadaEn: string | null
}

/** Lo que ve CUALQUIERA. Sin autor y sin estado interno: eso no viaja a proposito. */
export interface ResenaPublica {
  id: string
  calificacion: number
  comentario: string
  aspectos: string[]
  tipo: TipoResena
  origen: OrigenDeResena
  editada: boolean
  publicadaEn: string
  respuesta: RespuestaPublica | null
}

/**
 * API-65. Una reseña MIA: lo que necesito para reconocerla, llegar a la ficha de quien reseñe y
 * saber si todavia puedo cambiarla. Trae el estado porque una reseña retirada sigue siendo mia y
 * merece explicacion, no desaparecer de la lista sin que nadie diga nada.
 */
export interface MiResena {
  id: string
  perfilNombre: string
  perfilSlug: string
  calificacion: number
  comentario: string
  aspectos: string[]
  tipo: TipoResena
  origen: OrigenDeResena
  estado: EstadoResena
  editada: boolean
  publicadaEn: string
  editableHasta: string
}

export interface PaginaDeResenas {
  resenas: ResenaPublica[]
  siguienteCursor: string | null
}

/**
 * CU-16 4E. El servidor contesta 422 con los fragmentos que parecen datos personales ANTES de
 * publicar. No es un error del formulario: es una pregunta, y por eso tiene su propio tipo.
 */
export interface AvisoDeDatosPersonales {
  fragmentos: string[]
  mensaje: string
}

/** Publicar o editar termina de UNA de estas dos formas, nunca de las dos. */
export type Desenlace =
  | { tipo: 'publicada'; resena: Resena }
  | { tipo: 'datos-personales'; aviso: AvisoDeDatosPersonales }

const IDEMPOTENCIA = 'Idempotency-Key'

/**
 * Una clave por INTENCION (CC-04), como en la agenda: nace al abrir el formulario y se reusa en
 * cada reintento del mismo envio. Generarla en el clic convertiria un doble clic en dos
 * intenciones distintas, que es justo lo que la clave existe para distinguir.
 */
export function nuevaClaveDeIdempotencia(): string {
  return globalThis.crypto.randomUUID()
}

// ---------------------------------------------------------------- llamadas

/** API-46. Deja constancia del contacto y arranca el reloj de los 7 dias. Devuelve su id. */
export async function registrarContacto(
  perfilId: string, origen: OrigenContacto, canal: CanalContacto,
): Promise<string> {
  const r = await enviar<{ id: string }>('/api/contactos', { perfilId, origen, canal })
  return r.id
}

/** API-47. Un "no" es una respuesta con motivo, no un error: siempre llega 200. */
export function elegibilidad(perfilId: string): Promise<Elegibilidad> {
  return obtener<Elegibilidad>(`/api/mi/elegibilidad?perfilId=${encodeURIComponent(perfilId)}`)
}

/** API-48. La clave la pone quien llama, porque tiene que sobrevivir a los reintentos. */
export function publicar(
  contactoId: string, calificacion: number, comentario: string, aspectos: string[],
  clave: string, enviarIgual = false,
): Promise<Desenlace> {
  return conAvisoDeDatos(() => enviar<Resena>(
    '/api/resenas',
    { contactoId, calificacion, comentario, aspectos, enviarIgual },
    'POST',
    { [IDEMPOTENCIA]: clave },
  ))
}

/** API-49 · CS-16. Solo dentro de las 72 h; pasado el plazo el servidor responde que ya terminó. */
export function editar(
  resenaId: string, calificacion: number, comentario: string, aspectos: string[],
  enviarIgual = false,
): Promise<Desenlace> {
  return conAvisoDeDatos(() => enviar<Resena>(
    `/api/resenas/${encodeURIComponent(resenaId)}`,
    { calificacion, comentario, aspectos, enviarIgual },
    'PUT',
  ))
}

/** API-50. Una respuesta por reseña; volver a enviarla la edita (RN-S5-06). */
export function responder(resenaId: string, texto: string): Promise<{ creada: boolean }> {
  return enviar<{ creada: boolean }>(
    `/api/resenas/${encodeURIComponent(resenaId)}/respuesta`, { texto })
}

/** API-51. Un motivo grave la oculta en el acto (RN-S5-08). Responde 204: sin cuerpo. */
export function reportar(
  resenaId: string, motivo: MotivoDeReporte, detalle: string | null,
): Promise<void> {
  return enviar<void>(`/api/resenas/${encodeURIComponent(resenaId)}/reporte`, {
    motivo, detalle: detalle && detalle.trim() !== '' ? detalle.trim() : null,
  })
}

/**
 * API-65. Las que escribi, de la mas nueva a la mas vieja. EL ORDEN ES CONTRATO: lo garantiza el
 * servidor y la pantalla no vuelve a ordenar, para que no haya dos criterios que puedan discrepar.
 * El autor lo pone la sesion: no hay parametro con el que pedir las de otra persona.
 */
export function misResenas(): Promise<MiResena[]> {
  return obtener<MiResena[]>('/api/mi/resenas')
}

/** Lectura publica de las reseñas de una ficha. Sin sesion, igual que la ficha. */
export function resenasPublicas(slug: string, cursor?: string | null): Promise<PaginaDeResenas> {
  const q = cursor ? `?cursor=${encodeURIComponent(cursor)}` : ''
  return obtener<PaginaDeResenas>(`/api/prestadores/${encodeURIComponent(slug)}/resenas${q}`)
}

/**
 * El 422 de CU-16 4E no es un fallo: es "revisa esto antes de publicar". Se traduce a un
 * desenlace para que la pantalla no tenga que mirar codigos HTTP, y cualquier otro error sigue
 * siendo un error.
 */
async function conAvisoDeDatos(llamar: () => Promise<Resena>): Promise<Desenlace> {
  try {
    return { tipo: 'publicada', resena: await llamar() }
  } catch (e) {
    if (e instanceof ErrorApi && e.estado === 422 && e.datos.requiereConfirmacion === true) {
      // Ahora que este 422 viaja como problem+json (api-rest.md), su texto esta en `detail` y el
      // cliente ya lo dejo en `e.mensaje`: rescatarlo a mano seria codigo que no vigila nada. Lo
      // unico propio de este caso son los `fragmentos`, que viajan como extension del problema.
      const crudos = e.datos.fragmentos
      return {
        tipo: 'datos-personales',
        aviso: {
          fragmentos: Array.isArray(crudos) ? crudos.map(String) : [],
          mensaje: e.mensaje,
        },
      }
    }
    throw e
  }
}
