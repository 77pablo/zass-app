// De 'vitest/config', no de 'vite': el defineConfig de vite no conoce la
// seccion `test` y la deja sin revisar, asi que un error ahi no se ve.
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],

  server: {
    port: 5173,
    // El navegador habla solo con 5173, y Vite reenvia /api a la API de .NET.
    // Asi el frontend y la API comparten origen en desarrollo: las cookies de
    // sesion viajan sin tocar CORS, y SameSite=Lax sigue protegiendo igual que
    // en produccion. Si se apuntara directo a 5052, el navegador descartaria la
    // cookie y el fallo pareceria "la contrasena esta mala".
    proxy: {
      '/api': {
        target: 'http://localhost:5052',
        changeOrigin: false,
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
