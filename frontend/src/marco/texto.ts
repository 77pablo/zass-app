/** "1 persona" / "12 personas": el plural bien dicho vale mas que un "persona(s)". */
export function personas(n: number): string {
  return n === 1 ? '1 persona' : `${n} personas`
}

export function capitalizar(texto: string): string {
  return texto.charAt(0).toUpperCase() + texto.slice(1)
}

/**
 * RN-S5-11: con menos de 3 resenas el promedio no es publico y la API lo manda nulo. Aqui
 * no se inventa uno: se muestra el recuento. La coma decimal se pone a mano y no con
 * toLocaleString, para que no dependa de los datos de idioma del navegador.
 */
export function reputacion(promedio: number | null, totalResenas: number): string {
  const resenas = totalResenas === 1 ? '1 reseña' : `${totalResenas} reseñas`
  if (promedio !== null) return `${promedio.toFixed(1).replace('.', ',')} de 5 · ${resenas}`
  return totalResenas === 0 ? 'Sin reseñas todavía' : resenas
}

/** Las iniciales de una persona: hasta que haya fotos, es su cara en la tarjeta. */
export function iniciales(nombre: string): string {
  const letras = nombre
    .split(/\s+/)
    .filter((p) => /^\p{L}/u.test(p))
    .slice(0, 2)
    .map((p) => p.charAt(0).toLocaleUpperCase('es-CL'))
  return letras.join('') || '?'
}

/** 15000 -> "$15.000". A mano: no depende de los datos de idioma del navegador. */
export function pesos(n: number): string {
  return `$${n.toString().replace(/\B(?=(\d{3})+(?!\d))/g, '.')}`
}

export function duracion(minutos: number): string {
  if (minutos < 60) return `${minutos} min`
  const h = Math.floor(minutos / 60)
  const m = minutos % 60
  return m === 0 ? `${h} h` : `${h} h ${m} min`
}

const TIPOS_DE_CREDENCIAL: Record<string, string> = {
  Titulo: 'Título', Certificacion: 'Certificación', Curso: 'Curso', Colegiatura: 'Colegiatura',
}

export function etiquetaDeTipo(tipo: string): string {
  return TIPOS_DE_CREDENCIAL[tipo] ?? tipo
}

/** La fecha de hoy como AAAA-MM-DD en hora LOCAL: es lo que entiende <input type="date" max>. */
export function hoyISO(): string {
  const d = new Date()
  const dos = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${dos(d.getMonth() + 1)}-${dos(d.getDate())}`
}

/** Lo que la gente suele buscar: sirve de ejemplo en el campo y de atajo al abrirlo vacio. */
export const EJEMPLOS = ['abogado', 'contadora', 'gasfiter', 'arquitecto', 'corredor de propiedades', 'electricista']

/** Minusculas y sin tildes ni dieresis: «Ñuble» y «nuble» son lo mismo cuando alguien filtra una lista. */
export function sinTildes(texto: string): string {
  return texto.normalize('NFD').replace(/\p{M}/gu, '').toLocaleLowerCase('es-CL')
}
