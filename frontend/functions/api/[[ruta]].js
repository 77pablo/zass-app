/**
 * Cloudflare Pages Function: lo que llega a zass.cl/api/... se reenvia a la API.
 *
 * El navegador sigue hablando con UN solo origen (zass.cl), asi que las cookies de sesion (httpOnly, Secure,
 * SameSite=Lax) viajan sin tocar CORS, igual que en desarrollo con el proxy de Vite.
 *
 * Necesita la variable de entorno API_ORIGEN (la URL de Render, https://<servicio>.onrender.com). Sin ella responde 503 y
 * no intenta nada: mejor un error claro que reenviar a un sitio equivocado.
 *
 * IMPORTANTE: la API calcula el limite de peticiones por IP. Detras de este proxy todas las peticiones llegan
 * desde Cloudflare, asi que la API tiene que leer X-Forwarded-For (que aqui se completa con la IP real del
 * visitante) o TODA la ciudad compartira un solo contador. Ver DESPLIEGUE-CLOUDFLARE.md.
 *
 * P-11: con la variable SECRETO_ORIGEN, se agrega X-Origen-Zass (la API exige esa cabecera si
 * tiene Zass:SecretoDeOrigen). Lo que el visitante haya mandado con ese nombre se borra.
 */
import { ponerSecretoDeOrigen } from '../_comun/origen.js'

export async function onRequest({ request, env }) {
  if (!env.API_ORIGEN) {
    return new Response(
      JSON.stringify({ title: 'API no configurada', detail: 'Falta la variable API_ORIGEN.' }),
      { status: 503, headers: { 'content-type': 'application/json' } },
    )
  }

  const entrada = new URL(request.url)
  const destino = new URL(entrada.pathname + entrada.search, env.API_ORIGEN)

  const peticion = new Request(destino, request)
  // La IP real del visitante, para el limite de tasa y la auditoria. Se SOBREESCRIBE: lo que el visitante
  // haya mandado en estas cabeceras no se cree.
  const ip = request.headers.get('CF-Connecting-IP')
  if (ip) peticion.headers.set('X-Forwarded-For', ip)
  peticion.headers.set('X-Forwarded-Proto', 'https')
  peticion.headers.set('X-Forwarded-Host', entrada.host)
  // P-11: la prueba, para la API, de que esto paso por aqui (ver _comun/origen.js).
  ponerSecretoDeOrigen(peticion.headers, env)

  return fetch(peticion)
}
