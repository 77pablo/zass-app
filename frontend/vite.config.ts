// De 'vitest/config', no de 'vite': el defineConfig de vite no conoce la
// seccion `test` y la deja sin revisar, asi que un error ahi no se ve.
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

// El mismo certificado de desarrollo que usa la API (CN=localhost, el de
// `dotnet dev-certs`). Compartirlo tiene una razon concreta: se confia UNA vez y
// vale para los dos puertos. Si Vite generara el suyo, el navegador avisaria dos
// veces y en produccion ese aviso no existe.
//
// Si falta, se levanta en HTTP y se avisa, en vez de reventar: no todo el mundo
// que clone el repositorio tendra el certificado exportado todavia.
// import.meta.dirname y no __dirname: Vite avisa de que lo segundo deja de
// funcionar con el cargador de configuracion que sera el de por omision.
const carpetaCert = resolve(import.meta.dirname, '..', 'certificados')
function certificado() {
  try {
    return {
      cert: readFileSync(resolve(carpetaCert, 'localhost.pem')),
      key: readFileSync(resolve(carpetaCert, 'localhost.key')),
    }
  } catch {
    console.warn(
      [
        '',
        '  Sin certificado en certificados/: se levanta en HTTP.',
        '  Para HTTPS, desde la raiz del proyecto:',
        '    dotnet dev-certs https --export-path certificados/localhost.pem' +
          ' --format PEM --no-password',
        '',
      ].join('\n'),
    )
    return undefined
  }
}

export default defineConfig({
  plugins: [react()],

  server: {
    port: 5173,
    https: certificado(),
    // El navegador habla solo con 5173, y Vite reenvia /api a la API de .NET.
    // Asi el frontend y la API comparten origen en desarrollo: las cookies de
    // sesion viajan sin tocar CORS, y SameSite=Lax sigue protegiendo igual que
    // en produccion. Si se apuntara directo al 7277, el navegador descartaria la
    // cookie y el fallo pareceria "la contrasena esta mala".
    proxy: {
      '/api': {
        target: 'https://localhost:7277',
        changeOrigin: false,
        // El certificado es de desarrollo y no lo firma ninguna autoridad: Node
        // lo rechazaria. El navegador si lo acepta, porque se confia a mano.
        // Esto afecta SOLO al salto Vite -> API dentro de la misma maquina.
        secure: false,
      },
    },
  },

  test: {
    // Los archivos de prueba se llaman *.prueba.ts(x), en espanol como el
    // resto del proyecto. Sin esta linea Vitest busca *.test.ts y no encuentra nada,
    // que es peor que fallar: dice "0 pruebas" y sale con exito.
    include: ['src/**/*.prueba.{ts,tsx}'],
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/configuracion-pruebas.ts'],
    css: false,
  },
})
