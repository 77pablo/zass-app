import { Navigate, Route, Routes } from 'react-router-dom'
import { Marco } from './marco/Marco'
import { Comunas } from './paginas/Comunas'
import { CrearCuenta } from './paginas/CrearCuenta'
import { Entrar } from './paginas/Entrar'
import { Ficha } from './paginas/Ficha'
import { MiAgenda } from './paginas/MiAgenda'
import { MiFicha } from './paginas/MiFicha'
import { MisCitas } from './paginas/MisCitas'
import { NoEncontrada } from './paginas/NoEncontrada'
import { OfrecerServicios } from './paginas/OfrecerServicios'
import { Oficios } from './paginas/Oficios'
import { PaginaBuscar } from './paginas/PaginaBuscar'
import { Portada } from './paginas/Portada'
import { Privacidad } from './paginas/Privacidad'
import { Profesionales } from './paginas/Profesionales'
import { Terminos } from './paginas/Terminos'
import { RecuperarContrasena, RestablecerContrasena, VerificarCorreo } from './paginas/Recuperacion'
import { AvisoDeTarjeta, TarjetaEnMayusculas } from './paginas/AvisoDeTarjeta'

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
        <Route path="comunas" element={<Comunas />} />
        {/* La pagina se llamaba «barrios» cuando solo existia Temuco: los enlaces viejos siguen funcionando. */}
        <Route path="barrios" element={<Navigate to="/comunas" replace />} />
        <Route path="oficios" element={<Oficios />} />
        <Route path="profesionales" element={<Profesionales />} />
        <Route path="terminos" element={<Terminos />} />
        <Route path="privacidad" element={<Privacidad />} />
        <Route path="entrar" element={<Entrar />} />
        <Route path="crear-cuenta" element={<CrearCuenta />} />
        <Route path="mi-ficha" element={<MiFicha />} />
        <Route path="mi-agenda" element={<MiAgenda />} />
        <Route path="mis-citas" element={<MisCitas />} />
        <Route path="ofrecer-servicios" element={<OfrecerServicios />} />
        <Route path="verificar-correo" element={<VerificarCorreo />} />
        <Route path="recuperar-contrasena" element={<RecuperarContrasena />} />
        <Route path="restablecer-contrasena" element={<RestablecerContrasena />} />
        <Route path="tarjeta/no-valida" element={<AvisoDeTarjeta aviso="no-valida" />} />
        <Route path="tarjeta/no-disponible" element={<AvisoDeTarjeta aviso="no-disponible" />} />
        <Route path="tarjeta/sin-activar" element={<AvisoDeTarjeta aviso="sin-activar" />} />
        <Route path="t/:segmento" element={<TarjetaEnMayusculas />} />
        <Route path="*" element={<NoEncontrada />} />
      </Route>
    </Routes>
  )
}
