import { obtener } from '../../api/cliente'

// El catalogo (rubros, zonas) lo comparte con el perfil del prestador, asi que vive en
// api/catalogo. Se vuelve a exportar desde aqui para que la pantalla de busqueda siga
// importando todo de su propio servicio.
export { listarRubros, listarZonas } from '../../api/catalogo'
export type { Rubro, Zona } from '../../api/catalogo'

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

/** API-05. La ficha publica completa. Lo que el prestador eligio mostrar y nada mas. */
export interface FichaPublica {
  id: string
  slug: string
  nombrePublico: string
  descripcion: string | null
  urlFoto: string | null
  rubro: { id: number; codigo: string; nombre: string; icono: string | null } | null
  zonas: { nombre: string; sector: string; esPrincipal: boolean }[]
  servicios: {
    /** Id de la oferta: lo que pide el calendario (API-35). */
    id: string
    nombre: string
    duracionMinutos: number
    precioReferencia: number | null
    /** 'Automatico' confirma al instante; 'Manual' espera al prestador (CU-12 4C). */
    modoAgenda: 'Automatico' | 'Manual'
  }[]
  /** Sin estado interno ni archivos: solo "Verificada" o "Declarada por el usuario". */
  credenciales: { tipo: string; nombre: string; entidad: string | null; obtenidaEn: string; etiqueta: string; verificada: boolean }[]
  /** Nulo con menos de 3 resenas (RN-S5-11). */
  promedioCalificacion: number | null
  totalResenas: number
  contacto: {
    telefono: string | null
    whatsapp: string | null
    correo: string | null
    sitioWeb: string | null
  }
  publicadoEn: string | null
  /** Verdadero si no hay sesion: buscar es publico, pero el contacto se ve solo con cuenta. */
  contactoRequiereCuenta?: boolean
}

export function fichaPublica(slug: string): Promise<FichaPublica> {
  return obtener<FichaPublica>(`/api/prestadores/${encodeURIComponent(slug)}`)
}

// ---------------------------------------------------------------- la barra de busqueda (API-63)

export type OrdenDeTexto = 'Recomendados' | 'Cercania' | 'PrecioAsc' | 'PrecioDesc'

/**
 * Una persona en los resultados de la barra. Como en API-01: la distancia es TEXTO redondeado
 * ("a 3 km") y solo existe si la persona dio su ubicacion; el tipo no tiene donde guardar coordenadas.
 */
export interface ResultadoDeTexto {
  perfilId: string
  slug: string
  nombrePublico: string
  descripcion: string | null
  urlFoto: string | null
  rubro: string | null
  zonaPrincipal: string | null
  distancia: string | null
  tieneCredencialVerificada: boolean
  /** Nulo con menos de 3 resenas (RN-S5-11). */
  promedioCalificacion: number | null
  totalResenas: number
  /** El servicio mas barato que ofrece, en pesos, si puso precio. */
  precioDesde: number | null
}

export interface PaginaDeTexto {
  resultados: ResultadoDeTexto[]
  total: number
  pagina: number
  hayMas: boolean
  /** El orden que SE aplico: puede no ser el pedido (cercania sin ubicacion). */
  ordenAplicado: OrdenDeTexto
  conUbicacion: boolean
  aviso: string | null
}

export interface CriteriosDeTexto {
  q?: string
  orden?: OrdenDeTexto
  lat?: number
  lon?: number
  zona?: number
  pagina?: number
  /** Solo quien tiene una credencial verificada. */
  verificados?: boolean
  /** Nota minima de 1 a 5; solo cuenta a quien ya tiene 3 resenas (RN-S5-11). */
  calificacion?: number
}

/** API-63. Texto libre: un oficio, un servicio o un nombre. Sin texto devuelve a todos. */
export function buscarTexto(c: CriteriosDeTexto): Promise<PaginaDeTexto> {
  const q = new URLSearchParams()
  if (c.q && c.q.trim() !== '') q.set('q', c.q.trim())
  if (c.orden) q.set('orden', c.orden)
  if (c.lat !== undefined && c.lon !== undefined) {
    q.set('lat', String(c.lat))
    q.set('lon', String(c.lon))
  }
  if (c.zona !== undefined) q.set('zona', String(c.zona))
  if (c.pagina !== undefined && c.pagina > 1) q.set('pagina', String(c.pagina))
  // Solo viaja lo que de verdad se pidio: un `verificados=false` ensuciaria la URL de una busqueda compartida.
  if (c.verificados) q.set('verificados', 'true')
  if (c.calificacion !== undefined) q.set('calificacion', String(c.calificacion))
  const texto = q.toString()
  return obtener<PaginaDeTexto>(texto === '' ? '/api/buscar' : `/api/buscar?${texto}`)
}
