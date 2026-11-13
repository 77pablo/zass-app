/**
 * Los textos visibles de /mis-resenas (y el rotulo de origen, que tambien usa la tarjeta
 * publica), juntos y en un archivo aparte.
 *
 * PENDIENTES DE LA APROBACION DE PABLO. Son una PROPUESTA del agente que construyo la pantalla,
 * no textos aprobados: estan aqui, separados del componente, para que se puedan leer de corrido
 * y cambiar sin tocar la logica. Mientras no los apruebe, cualquiera de ellos puede cambiar.
 *
 * Van en un modulo propio y NO dentro del `.tsx` a proposito: un archivo que exporta componentes
 * y ademas constantes pierde la recarga en caliente (regla `only-export-components` de oxlint).
 *
 * Vive en `funciones/resenas/` y no en `paginas/` porque el rotulo de origen lo pinta tambien
 * `componentes/resenas/TarjetaDeResena`: un componente no deberia importar de una pagina.
 *
 * Lo UNICO que no se decide aqui son los mensajes de error del servidor: esos los escribe MOD-11,
 * que es quien conoce la regla, y la pantalla los muestra tal cual.
 */

export const TITULO = 'Mis reseñas'

/** CS-16 en una frase, arriba del todo: la regla que explica por que esta pantalla existe. */
export const INTRO =
  'Puedes cambiar una reseña durante las 72 horas siguientes a publicarla. Después queda como está.'

export const CARGANDO = 'Cargando tus reseñas…'
export const ERROR_CARGA = 'No pudimos cargar tus reseñas.'
export const REINTENTAR = 'Reintentar'

export const VACIO = 'Todavía no has dejado ninguna reseña'
export const VACIO_DETALLE = 'Cuando alguien te atienda, cuéntale al próximo cómo te fue.'
export const VACIO_ENLACE = 'Buscar a alguien'

/**
 * El plazo se dice con la FECHA Y HORA exactas, no con un "quedan 3 horas": una cuenta atras
 * pintada una vez miente en cuanto la pestaña lleva un rato abierta, y aqui se llega justamente
 * volviendo al dia siguiente.
 */
export const plazoVivo = (cuando: string): string => `Puedes editarla hasta el ${cuando}`
export const PLAZO_VENCIDO = 'El plazo para editarla terminó'

export const EDITAR = 'Editar mi reseña'
export const EDITAR_TITULO = 'Editar tu reseña'
export const GUARDAR = 'Guardar los cambios'

/** Lo que paso con una reseña que ya no esta publicada. Ninguna desaparece de la lista en silencio. */
export const ESTADO_RETIRADA = 'Retirada tras una revisión'
export const ESTADO_OCULTA = 'Oculta mientras un moderador la revisa'

/**
 * Frase literal de la decision de Pablo del 11-nov-2026.
 *
 * Solo se rotula el contacto DIRECTO; la reseña nacida de una cita NO lleva etiqueta, por dos
 * razones. La primera es que un rotulo que sale en casi todas las tarjetas deja de leerse, y el
 * `origen` existe justamente para señalar la excepcion: "ZASS! no vio pasar este servicio por su
 * agenda". La segunda es que «Con cita en ZASS!» se lee como un sello de calidad, y ZASS! no
 * comprueba que el servicio saliera bien: solo que hubo una hora agendada. Seria prometer mas de
 * lo que el dato sostiene, que es lo que el resto del sitio se cuida de no hacer.
 */
export const ORIGEN_DIRECTO = 'Contacto directo, sin cita'

export const EDITADA = 'editada'
export const POR_INASISTENCIA = 'Reseña por inasistencia: no habla del servicio.'
