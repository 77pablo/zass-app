import { Route, Routes } from 'react-router-dom'
import { Marco } from './marco/Marco'
import { Barrios } from './paginas/Barrios'
import { CrearCuenta } from './paginas/CrearCuenta'
import { Entrar } from './paginas/Entrar'
import { Ficha } from './paginas/Ficha'
import { MiFicha } from './paginas/MiFicha'
import { NoEncontrada } from './paginas/NoEncontrada'
import { Oficios } from './paginas/Oficios'
import { PaginaBuscar } from './paginas/PaginaBuscar'
import { Portada } from './paginas/Portada'
import { Privacidad } from './paginas/Privacidad'
import { Profesionales } from './paginas/Profesionales'
import { Terminos } from './paginas/Terminos'
import { RecuperarContrasena, RestablecerContrasena, VerificarCorreo } from './paginas/Recuperacion'

/**
 * Un sitio de varias paginas, no una pagina larga: cada seccion vive en su propia ruta y se
 * abre desde las demas. Las rutas son las direcciones que alguien puede compartir.
 */
export function App() {
  return (
    <Routes>
      <Route element={<Marco />}>
        <Route index element={<Portada />} />
        <Route path="buscar" element={<PaginaBuscar />} />
        <Route path="prestadores/:slug" element={<Ficha />} />
        <Route path="barrios" element={<Barrios />} />
        <Route path="oficios" element={<Oficios />} />
        <Route path="profesionales" element={<Profesionales />} />
        <Route path="terminos" element={<Terminos />} />
        <Route path="privacidad" element={<Privacidad />} />
        <Route path="entrar" element={<Entrar />} />
        <Route path="crear-cuenta" element={<CrearCuenta />} />
        <Route path="mi-ficha" element={<MiFicha />} />
        <Route path="verificar-correo" element={<VerificarCorreo />} />
        <Route path="recuperar-contrasena" element={<RecuperarContrasena />} />
        <Route path="restablecer-contrasena" element={<RestablecerContrasena />} />
        <Route path="*" element={<NoEncontrada />} />
      </Route>
    </Routes>
  )
}
