/** /sitemap.xml: la portada, las paginas fijas y todas las fichas publicadas (o un indice si pasan de 50 000). */
import { servirSitemap } from './_comun/sitemap.js'

export const onRequest = (contexto) => servirSitemap(contexto, 0)
