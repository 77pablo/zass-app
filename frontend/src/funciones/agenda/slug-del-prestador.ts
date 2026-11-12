import { buscarTexto } from '../busqueda/servicio'
import type { Cita } from './servicio'

/**
 * La direccion publica (`slug`) del prestador de una cita.
 *
 * Hace falta porque el calendario (API-35) vive en `/api/prestadores/{slug}/bloques` y
 * **API-37 no devuelve el slug**: `CitaDto` trae `perfilPrestadorId` y el nombre, nada mas. Sin
 * esto, el paso 3 de CU-12 («muestra el calendario del prestador») no se puede dar desde «Mis
 * citas». Lo correcto es que el servidor lo incluya; mientras no lo haga, se resuelve aqui:
 *
 * 1. Si la cita es MIA como prestador, el slug ya lo tengo: es el de mi propio perfil (API-18).
 * 2. Si no, se busca por el nombre en el buscador publico (API-63) y **se acepta solo el
 *    resultado cuyo `perfilId` coincide**. Comparar por nombre daria el calendario de otra
 *    persona cuando dos se llaman igual; comparar por identificador no puede equivocarse.
 *
 * Devuelve `null` cuando no se encuentra (por ejemplo, un perfil que ya no esta publicado). Ese
 * caso no tiene arreglo en la pantalla: su calendario publico tampoco existiria.
 */
export async function slugDelPrestador(
  cita: Pick<Cita, 'perfilPrestadorId' | 'prestador'>,
  miPerfil: { id: string; slug: string } | null,
): Promise<string | null> {
  if (miPerfil !== null && miPerfil.id === cita.perfilPrestadorId) return miPerfil.slug

  const pagina = await buscarTexto({ q: cita.prestador })
  const suyo = pagina.resultados.find((r) => r.perfilId === cita.perfilPrestadorId)
  return suyo?.slug ?? null
}
