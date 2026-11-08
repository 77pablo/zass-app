import { Link } from 'react-router-dom'
import '../legal.css'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'
import { AvisoDeBorrador } from './AvisoDeBorrador'
import { DatoLegal } from './DatoLegal'
import { CORREO_PRIVACIDAD, PLAZO_REGISTROS_DE_SEGURIDAD, PROVEEDORES, RESPONSABLE, ULTIMA_ACTUALIZACION } from './legal'

/**
 * Politica de privacidad. Cada afirmacion se corresponde con algo que el sistema hace hoy (y quedo
 * revisado contra el codigo al escribirla): hash de contrasena, cookies httpOnly, ubicacion que no se
 * guarda, direccion cifrada, anonimizacion al eliminar la cuenta. Lo que NO existe todavia no se promete.
 * Borrador para revision de un abogado (ver `legal.ts`); no es asesoria legal.
 */
export function Privacidad() {
  useTitulo('Política de privacidad')
  useMeta({ descripcion: 'Qué datos guarda ZASS!, para qué, cuánto tiempo, y cómo ejercer tus derechos según la Ley 21.719. Sin publicidad ni seguimiento de terceros.' })

  return (
    <article className="legal">
      <div className="contenedor">
        <AvisoDeBorrador />
        <h1>Política de privacidad</h1>
        <p className="legal-fecha">Última actualización: {ULTIMA_ACTUALIZACION}</p>

        <h2>1. Quién es el responsable</h2>
        <p>
          El responsable del tratamiento de tus datos personales es <DatoLegal valor={RESPONSABLE} />. Para
          cualquier consulta sobre tus datos, escribe a <DatoLegal valor={CORREO_PRIVACIDAD} comoCorreo />.
        </p>

        <h2>2. Qué datos guardamos</h2>
        <table className="legal-tabla">
          <thead>
            <tr><th scope="col">Quién</th><th scope="col">Qué guardamos</th></tr>
          </thead>
          <tbody>
            <tr>
              <th scope="row">Toda cuenta</th>
              <td>
                Nombre, correo, tu contraseña (solo como una huella cifrada irreversible: ni nosotros podemos
                leerla), si confirmaste tu correo, tu tipo de cuenta y las fechas de creación y cambio.
              </td>
            </tr>
            <tr>
              <th scope="row">Quien busca</th>
              <td>
                Nada más, salvo que configures una zona y un radio de preferencia en tu perfil: eso sí se guarda
                para recordarlo.
              </td>
            </tr>
            <tr>
              <th scope="row">Quien ofrece un servicio</th>
              <td>
                Lo que escribes en tu ficha, que es <strong>público</strong>: nombre público, descripción,
                oficio, servicios con su duración y precio de referencia, barrios donde atiendes y los datos de
                contacto que elijas mostrar (teléfono, WhatsApp, correo público, sitio web). También guardamos
                las credenciales que declares (tipo, nombre, entidad y fechas).
              </td>
            </tr>
            <tr>
              <th scope="row">Quien ofrece, si lo indica</th>
              <td>
                Una dirección y coordenadas. La dirección se guarda cifrada. Ninguna de las dos se muestra en tu
                ficha ni se entrega al público: las coordenadas solo sirven para calcular distancias aproximadas
                («a 3 km») y nunca se muestran exactas.
              </td>
            </tr>
            <tr>
              <th scope="row">Datos técnicos</th>
              <td>
                Al iniciar sesión guardamos la dirección IP y el tipo de navegador de esa sesión, y llevamos un
                registro de acciones relevantes (por ejemplo entrar, cambiar la contraseña o eliminar la cuenta)
                con su fecha e IP. Es por seguridad y para limitar abusos.
              </td>
            </tr>
          </tbody>
        </table>

        <h2>3. Tu ubicación al buscar</h2>
        <p>
          Si usas «cerca de mí», tu navegador te pide permiso y, si aceptas, envía tu ubicación al servidor solo
          para ordenar los resultados por cercanía en esa búsqueda. <strong>No la guardamos</strong>. Puedes decir
          que no y buscar por barrio.
        </p>

        <h2>4. Cookies y almacenamiento en tu navegador</h2>
        <p>Solo usamos lo estrictamente necesario para que puedas entrar y seguir dentro de tu cuenta:</p>
        <ul>
          <li><strong>zass_sesion</strong>: tu sesión, dura 15 minutos y se renueva mientras uses el sitio.</li>
          <li><strong>zass_refresco</strong>: permite renovar la sesión sin pedirte la contraseña de nuevo; dura hasta 30 días.</li>
          <li>Las dos son «httpOnly» (el código de la página no puede leerlas), solo viajan por HTTPS y no se envían desde otros sitios.</li>
          <li><strong>zass-sesion</strong> (almacenamiento local): una marca que solo dice «aquí hubo una sesión abierta»; no contiene datos tuyos.</li>
        </ul>
        <p>
          <strong>No usamos publicidad ni herramientas de seguimiento o analítica de terceros.</strong> Un detalle
          técnico: las letras del sitio se cargan desde Google Fonts, así que Google recibe tu dirección IP al
          abrir una página.
        </p>

        <h2>5. Para qué usamos tus datos</h2>
        <ul>
          <li>Darte una cuenta y mantenerla segura, y mostrar la ficha de quien ofrece un servicio (ejecutar el servicio que pides).</li>
          <li>Enviarte los correos que el sitio necesita: confirmar tu correo y recuperar tu contraseña.</li>
          <li>Prevenir abusos y proteger el sitio (interés legítimo en la seguridad).</li>
          <li>Cumplir obligaciones legales cuando corresponda.</li>
        </ul>
        <p>No vendemos tus datos ni los usamos para publicidad.</p>

        <h2>6. Con quién los compartimos</h2>
        <p>
          La ficha pública la ve cualquiera. Para que el sitio funcione usamos proveedores de infraestructura
          (alojamiento del sitio y de la API, base de datos y envío de correos), que tratan los datos solo por
          encargo nuestro. Proveedores: <DatoLegal valor={PROVEEDORES} />. No
          entregamos datos a terceros para otros fines, salvo que una autoridad competente lo exija.
        </p>

        <h2>7. Cuánto tiempo los conservamos</h2>
        <ul>
          <li>Los de tu cuenta, mientras la tengas.</li>
          <li>
            Si pides eliminarla: reemplazamos tu nombre y tu correo, borramos tu contraseña, cerramos tus
            sesiones y despublicamos tu ficha. La cuenta queda como «usuario eliminado» para mantener la
            coherencia del historial de otras personas.
          </li>
          <li>Los enlaces para confirmar el correo vencen a las 24 horas, y los de recuperar la contraseña, a la hora.</li>
          <li>
            Los registros de seguridad (IP y fecha de acciones) se conservan por <DatoLegal valor={PLAZO_REGISTROS_DE_SEGURIDAD} />.
          </li>
        </ul>

        <h2>8. Tus derechos (Ley 21.719)</h2>
        <p>Como titular de tus datos personales puedes pedirnos:</p>
        <ul>
          <li><strong>Acceso</strong>: saber qué datos tuyos tratamos y recibir una copia.</li>
          <li><strong>Rectificación</strong>: corregir datos inexactos o incompletos.</li>
          <li><strong>Supresión</strong>: que eliminemos tus datos cuando corresponda.</li>
          <li><strong>Oposición</strong>: que dejemos de tratar tus datos para ciertos fines.</li>
          <li><strong>Portabilidad</strong>: recibir tus datos en un formato estructurado y de uso común.</li>
        </ul>
        <p>
          Escribe a <DatoLegal valor={CORREO_PRIVACIDAD} comoCorreo /> indicando qué quieres y desde el correo
          de tu cuenta, para poder comprobar que eres tú. Si no quedas conforme con la respuesta, puedes acudir a
          la Agencia de Protección de Datos Personales.
        </p>

        <h2>9. Cambios</h2>
        <p>
          Si cambia algo de esta política, la publicaremos aquí con su fecha. Las reglas de uso del sitio están
          en los <Link to="/terminos">Términos de uso</Link>.
        </p>
      </div>
    </article>
  )
}
