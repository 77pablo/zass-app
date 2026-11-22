import { enviar, obtener } from '../../api/cliente'

/*
 * ES-3b · API-43 (CS-14). Por que canal llega cada aviso. La API guarda por tipo; la pantalla
 * muestra grupos: un grupo esta encendido si TODOS sus tipos lo estan, y al cambiarlo se mandan
 * todos sus tipos juntos.
 */

export interface PreferenciaTipo {
  tipo: string
  grupo: string
  correo: boolean
  push: boolean
  correoFijo: boolean
}

export interface GrupoDeAvisos {
  clave: string
  tipos: string[]
  correo: boolean
  push: boolean
  correoFijo: boolean
}

const RUTA = '/api/mi/notificaciones/preferencias'

export async function verPreferencias(): Promise<PreferenciaTipo[]> {
  return (await obtener<{ preferencias: PreferenciaTipo[] }>(RUTA)).preferencias
}

/** Agrupa en el orden en que llegan (el servidor ya los manda ordenados por grupo). */
export function agrupar(lista: PreferenciaTipo[]): GrupoDeAvisos[] {
  const grupos: GrupoDeAvisos[] = []
  for (const p of lista) {
    let g = grupos.find((x) => x.clave === p.grupo)
    if (!g) {
      g = { clave: p.grupo, tipos: [], correo: true, push: true, correoFijo: false }
      grupos.push(g)
    }
    g.tipos.push(p.tipo)
    g.correo = g.correo && p.correo
    g.push = g.push && p.push
    g.correoFijo = g.correoFijo || p.correoFijo
  }
  return grupos
}

/** Deja todo el grupo con estos canales. Devuelve como quedo todo, para no adivinar. */
export async function guardarGrupo(grupo: GrupoDeAvisos, correo: boolean, push: boolean): Promise<PreferenciaTipo[]> {
  const r = await enviar<{ preferencias: PreferenciaTipo[] }>(RUTA, {
    preferencias: grupo.tipos.map((tipo) => ({ tipo, correo: grupo.correoFijo || correo, push })),
  }, 'PUT')
  return r.preferencias
}
