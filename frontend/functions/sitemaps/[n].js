/**
 * /sitemaps/1.xml, /sitemaps/2.xml...: los archivos que enumera el indice cuando hay mas de 50 000 URLs.
 * Cada uno vuelve a leer la lista (la respuesta se cachea una hora en el borde, asi que es poco frecuente).
 */
import { servirSitemap } from '../_comun/sitemap.js'

export const onRequest = (contexto) => {
  const crudo = Array.isArray(contexto.params.n) ? contexto.params.n[0] : contexto.params.n
  // Acepta "2" y "2.xml" (el nombre del archivo sin la extension llega como parametro segun el enrutador).
  const numero = /^\d{1,4}(\.xml)?$/.test(crudo ?? '') ? Number.parseInt(crudo, 10) : -1
  return servirSitemap(contexto, numero)
}
