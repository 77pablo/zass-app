/** Una captura del primer pantallazo de la portada, a tamano real, para juzgar a ojo el hero y el radar. */
import { resolve } from 'node:path'
import { chromium } from 'playwright'

const carpeta = resolve(import.meta.dirname, '..', '..', 'registros', 'capturas')
const navegador = await chromium.launch()
for (const [nombre, tamano] of [['escritorio', { width: 1440, height: 900 }], ['celular', { width: 390, height: 844 }]]) {
  const pagina = await navegador.newPage({ viewport: tamano, ignoreHTTPSErrors: true })
  await pagina.goto('https://localhost:5173/', { waitUntil: 'networkidle' })
  await pagina.waitForTimeout(2500)
  await pagina.screenshot({ path: resolve(carpeta, `portada-${nombre}.png`) })
  if (nombre === 'escritorio') {
    await pagina.getByRole('combobox').fill('abogado')
    await pagina.waitForTimeout(1200)
    await pagina.screenshot({ path: resolve(carpeta, 'portada-sugerencias.png') })
  }
  await pagina.close()
}
await navegador.close()
