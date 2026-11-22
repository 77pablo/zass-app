/*
 * P-11: el secreto compartido entre las Functions y la API.
 *
 * La URL de Render esta en el repositorio publico. Sin esto, cualquiera le pide a la API
 * directo y se salta lo que hacen las Functions: el mantenimiento (_middleware.js) y la IP
 * real del visitante (X-Forwarded-For). Toda Function que llame a la API pone esta cabecera
 * con el valor de la variable SECRETO_ORIGEN del proyecto de Cloudflare Pages, y la API
 * (Zass:SecretoDeOrigen) responde 403 a lo que no la traiga.
 *
 * Sin la variable no se manda nada: asi este codigo se puede desplegar ANTES de que Pablo
 * ponga el secreto, y la API (que tampoco lo exige sin su propia variable) sigue igual.
 *
 * Lo que el VISITANTE haya mandado en esta cabecera se borra siempre: no se reenvia.
 */
export const CABECERA_DE_ORIGEN = 'X-Origen-Zass'

/** El secreto, o null si la variable no esta (o esta vacia). */
export function secretoDeOrigen(env) {
  const valor = env?.SECRETO_ORIGEN
  if (typeof valor !== 'string') return null
  const limpio = valor.trim()
  return limpio.length > 0 ? limpio : null
}

/**
 * Pone la cabecera en `cabeceras` (un Headers o un objeto simple) si hay secreto, y la quita
 * si no. Devuelve el mismo objeto.
 */
export function ponerSecretoDeOrigen(cabeceras, env) {
  const secreto = secretoDeOrigen(env)
  if (cabeceras instanceof Headers) {
    cabeceras.delete(CABECERA_DE_ORIGEN)
    if (secreto) cabeceras.set(CABECERA_DE_ORIGEN, secreto)
    return cabeceras
  }
  for (const nombre of Object.keys(cabeceras)) {
    if (nombre.toLowerCase() === CABECERA_DE_ORIGEN.toLowerCase()) delete cabeceras[nombre]
  }
  if (secreto) cabeceras[CABECERA_DE_ORIGEN] = secreto
  return cabeceras
}
