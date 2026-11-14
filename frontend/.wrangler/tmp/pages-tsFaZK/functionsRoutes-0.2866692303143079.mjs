import { onRequest as __prestadores__slug__js_onRequest } from "C:\\Users\\pdani\\zass-app-1\\frontend\\functions\\prestadores\\[slug].js"
import { onRequest as __sitemaps__n__js_onRequest } from "C:\\Users\\pdani\\zass-app-1\\frontend\\functions\\sitemaps\\[n].js"
import { onRequest as __t__segmento__js_onRequest } from "C:\\Users\\pdani\\zass-app-1\\frontend\\functions\\t\\[segmento].js"
import { onRequest as __api___ruta___js_onRequest } from "C:\\Users\\pdani\\zass-app-1\\frontend\\functions\\api\\[[ruta]].js"
import { onRequest as __sitemap_xml_js_onRequest } from "C:\\Users\\pdani\\zass-app-1\\frontend\\functions\\sitemap.xml.js"
import { onRequest as ___middleware_js_onRequest } from "C:\\Users\\pdani\\zass-app-1\\frontend\\functions\\_middleware.js"

export const routes = [
    {
      routePath: "/prestadores/:slug",
      mountPath: "/prestadores",
      method: "",
      middlewares: [],
      modules: [__prestadores__slug__js_onRequest],
    },
  {
      routePath: "/sitemaps/:n",
      mountPath: "/sitemaps",
      method: "",
      middlewares: [],
      modules: [__sitemaps__n__js_onRequest],
    },
  {
      routePath: "/t/:segmento",
      mountPath: "/t",
      method: "",
      middlewares: [],
      modules: [__t__segmento__js_onRequest],
    },
  {
      routePath: "/api/:ruta*",
      mountPath: "/api",
      method: "",
      middlewares: [],
      modules: [__api___ruta___js_onRequest],
    },
  {
      routePath: "/sitemap.xml",
      mountPath: "/",
      method: "",
      middlewares: [],
      modules: [__sitemap_xml_js_onRequest],
    },
  {
      routePath: "/",
      mountPath: "/",
      method: "",
      middlewares: [___middleware_js_onRequest],
      modules: [],
    },
  ]