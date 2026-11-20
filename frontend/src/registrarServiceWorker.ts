/**
 * Registra el service worker (public/sw.js) SOLO en produccion: en desarrollo, guardar
 * /assets/ haria que Vite sirviera archivos viejos y nadie entenderia por que no cambian.
 * Se registra despues de `load` para no competir con la primera carga de la pagina.
 * Si el navegador no lo soporta o el registro falla, el sitio funciona igual: no es esencial.
 */
export function registrarServiceWorker(
  nav: Navigator = navigator,
  ventana: Pick<Window, 'addEventListener'> = window,
  produccion: boolean = import.meta.env.PROD,
): boolean {
  if (!produccion || !('serviceWorker' in nav)) return false
  ventana.addEventListener('load', () => {
    nav.serviceWorker.register('/sw.js').catch(() => { /* sin service worker el sitio sigue igual */ })
  })
  return true
}
