import { obtener } from '../../api/cliente'

/** API-02. */
export interface Rubro {
  id: number
  codigo: string
  nombre: string
  icono: string | null
}

/** API-04. El centroide es dato publico: el centro de un barrio, no la casa de nadie. */
export interface Zona {
  id: number
  nombre: string
  sector: string
  comuna: string
  centroLatitud: number
  centroLongitud: number
}

/**
 * Un prestador como lo ve cualquiera (API-01).
 *
 * Lo que este tipo NO tiene es a proposito, y es el espejo de RN-S3-02: ni direccion,
 * ni latitud, ni longitud, ni correo. Y `distancia` es TEXTO ("a 3 km"), no un numero:
 * con la distancia exacta desde tres puntos se triangula donde vive alguien. Si algun
 * dia la API devolviera un numero aqui, el compilador lo senalaria.
 */
export interface Resultado {
  perfilId: string
  slug: string
  nombrePublico: string
  descripcion: string | null
  urlFoto: string | null
  rubroId: number
  zonaPrincipal: string | null
  distancia: string | null
  tieneCredencialVerificada: boolean
  /** Nulo con menos de 3 resenas (RN-S5-11): entonces solo se muestra el recuento. */
  promedioCalificacion: number | null
  totalResenas: number
  esTuPerfil: boolean
}

export interface PaginaDeBusqueda {
  resultados: Resultado[]
  cursorSiguiente: string | null
  ordenadoPorDistancia: boolean
  zonaBuscada: string | null
  /** Texto para mostrar tal cual: "Mostramos los de Centro", "El radio maximo es 50 km". */
  aviso: string | null
}

/**
 * Lo que se le pide a la busqueda. `zona` y `lat`/`lon` son alternativas: con punto se
 * ordena por distancia; con zona se filtra sin orden por distancia (RN-S3-07).
 */
export interface Criterios {
  rubro: string
  zona?: number
  lat?: number
  lon?: number
  /** En kilometros. Solo significa algo con punto: con zona no hay desde donde medirlo. */
  radio?: number
  verificados?: boolean
  calificacion?: number
  cursor?: string
}

export function listarRubros(): Promise<Rubro[]> {
  return obtener<Rubro[]>('/api/rubros')
}

export function listarZonas(): Promise<Zona[]> {
  return obtener<Zona[]>('/api/zonas')
}

/**
 * API-01. Solo viaja lo que de verdad se pidio: un `verificados=false` no cambia nada
 * para la API pero ensucia la URL de una busqueda que alguien va a compartir.
 *
 * URLSearchParams codifica el cursor. No es un detalle: es Base64, lleva `=` y puede
 * llevar `+`, y un `+` sin codificar llega al servidor como un espacio.
 */
export function buscar(c: Criterios): Promise<PaginaDeBusqueda> {
  const q = new URLSearchParams({ rubro: c.rubro })

  if (c.lat !== undefined && c.lon !== undefined) {
    q.set('lat', String(c.lat))
    q.set('lon', String(c.lon))
    // Solo con punto, y solo si se pidio: sin radio la API usa el suyo por omision.
    if (c.radio !== undefined) q.set('radio', String(c.radio))
  } else if (c.zona !== undefined) {
    q.set('zona', String(c.zona))
  }

  if (c.verificados) q.set('verificados', 'true')
  if (c.calificacion !== undefined) q.set('calificacion', String(c.calificacion))
  if (c.cursor) q.set('cursor', c.cursor)

  return obtener<PaginaDeBusqueda>(`/api/busqueda?${q.toString()}`)
}
