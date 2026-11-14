import { Navigate, Route, Routes } from 'react-router-dom'
import { Marco } from './marco/Marco'
import { Comunas } from './paginas/Comunas'
import { CrearCuenta } from './paginas/CrearCuenta'
import { DejarResena } from './paginas/DejarResena'
import { Entrar } from './paginas/Entrar'
import { Ficha } from './paginas/Ficha'
import { MiAgenda } from './paginas/MiAgenda'
import { MiFicha } from './paginas/MiFicha'
import { MisCitas } from './paginas/MisCitas'
import { MisResenas } from './paginas/MisResenas'
import { NoEncontrada } from './paginas/NoEncontrada'
import { OfrecerServicios } from './paginas/OfrecerServicios'
import { Oficios } from './paginas/Oficios'
import { PaginaBuscar } from './paginas/PaginaBuscar'
import { Portada } from './paginas/Portada'
import { Privacidad } from './paginas/Privacidad'
import { Profesionales } from './paginas/Profesionales'
import { PagoSimulado } from './paginas/PagoSimulado'
import { Planes, PlanesListo } from './paginas/Planes'
import { ResenasDelPrestador } from './paginas/ResenasDelPrestador'
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
        {/* S5: leer lo que dicen es publico; escribir una reseña pide cuenta y elegibilidad (CU-16). */}
        <Route path="prestadores/:slug/resenas" element={<ResenasDelPrestador />} />
        <Route path="prestadores/:slug/resena" element={<DejarResena />} />
        <Route path="comunas" element={<Comunas />} />
        {/* La pagina se llamaba «barrios» cuando solo existia Temuco: los enlaces viejos siguen funcionando. */}
        <Route path="barrios" element={<Navigate to="/comunas" replace />} />
        <Route path="oficios" element={<Oficios />} />
        <Route path="profesionales" element={<Profesionales />} />
        <Route path="planes" element={<Planes />} />
        <Route path="planes/listo" element={<PlanesListo />} />
        <Route path="pago/inscripcion" element={<PagoSimulado />} />
        <Route path="terminos" element={<Terminos />} />
        <Route path="privacidad" element={<Privacidad />} />
        <Route path="entrar" element={<Entrar />} />
        <Route path="crear-cuenta" element={<CrearCuenta />} />
        <Route path="mi-ficha" element={<MiFicha />} />
        <Route path="mi-agenda" element={<MiAgenda />} />
        <Route path="mis-citas" element={<MisCitas />} />
        {/* CS-16: sin esta ruta, la ventana de 72 h para editar una reseña no se puede alcanzar. */}
        <Route path="mis-resenas" element={<MisResenas />} />
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
