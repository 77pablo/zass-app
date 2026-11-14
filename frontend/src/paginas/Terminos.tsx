import { Link } from 'react-router-dom'
import '../legal.css'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'
import { AvisoDeBorrador } from './AvisoDeBorrador'
import { DatoLegal } from './DatoLegal'
import { CORREO_PRIVACIDAD, RESPONSABLE, ULTIMA_ACTUALIZACION } from './legal'

/**
 * Terminos de uso. Describe lo que ZASS! hace HOY: no hay pagos en linea, ni agenda, ni sello de
 * verificacion revisado en produccion, ni resenas escritas, y el texto no promete nada de eso. Es un
 * borrador para revision de un abogado (ver `legal.ts`), no asesoria legal.
 */
export function Terminos() {
  useTitulo('Términos de uso')
  useMeta({ descripcion: 'Las reglas para usar ZASS!: buscar es gratis, el profesional paga una mensualidad y cada persona responde por lo que publica.' })

  return (
    <article className="legal">
      <div className="contenedor">
        <AvisoDeBorrador />
        <h1>Términos de uso</h1>
        <p className="legal-fecha">Última actualización: {ULTIMA_ACTUALIZACION}</p>

        <h2>1. Qué es ZASS!</h2>
        <p>
          ZASS! es un sitio web que ayuda a encontrar a personas que ofrecen servicios cerca de ti y a
          contactarlas directamente. ZASS! pone en contacto a las dos partes; <strong>no presta los
          servicios, no es parte del acuerdo que hagas con quien contrates y no cobra por esa gestión</strong>.
          Hoy ZASS! no procesa pagos entre personas ni administra agendas o citas.
        </p>
        <p>El sitio lo opera <DatoLegal valor={RESPONSABLE} />.</p>

        <h2>2. Buscar es gratis; ofrecer tiene una mensualidad</h2>
        <p>
          Buscar y contactar a alguien no te cuesta nada, y no hace falta tener cuenta. Quien ofrece un servicio
          paga una mensualidad por mantener su ficha visible en las búsquedas. Al publicar su ficha parte con un
          <strong> primer mes gratis</strong>, y puede ver los planes y sus precios en{' '}
          <Link to="/planes">Planes y precios</Link>.
        </p>
        <p>
          Antes de cobrarte por primera vez te avisamos por correo con el valor y la fecha. Si no quieres seguir,
          tu ficha deja de aparecer en las búsquedas y tus datos, tus citas y tus reseñas siguen guardados. El
          precio de lanzamiento de los primeros cincuenta prestadores se mantiene mientras la suscripción siga
          activa; si se interrumpe, ese precio se pierde.
        </p>

        <h2>3. Tu cuenta</h2>
        <ul>
          <li>Debes dar datos verdaderos, y un correo al que tengas acceso: lo usamos para confirmar tu cuenta y para recuperarla.</li>
          <li>Eres responsable de guardar tu contraseña y de lo que se haga con tu cuenta. Si crees que alguien más entró, cámbiala y avísanos.</li>
          <li>Puedes pedir que eliminemos tu cuenta cuando quieras. Al hacerlo, tus datos personales se anonimizan y tu ficha deja de ser pública (ver la <Link to="/privacidad">Política de privacidad</Link>).</li>
        </ul>

        <h2>4. Lo que publicas</h2>
        <p>
          Si ofreces un servicio, lo que escribes en tu ficha (nombre, descripción, servicios, precios de
          referencia, barrios y datos de contacto) es público y es tu responsabilidad. Debe ser verdadero y no
          infringir derechos de nadie. Nos autorizas a mostrarlo en el sitio con ese fin, mientras tu ficha esté
          publicada.
        </p>
        <p>
          Las credenciales (títulos, certificados) que declares se muestran como «Declarada por el usuario».
          Hoy ZASS! todavía no revisa credenciales: no asumas que una credencial fue comprobada por ZASS!. Cuando
          exista un sello de verificación, se indicará de forma expresa y solo en las credenciales que lo tengan.
        </p>

        <h2>5. Lo que no se puede hacer</h2>
        <ul>
          <li>Hacerse pasar por otra persona o inventar credenciales, experiencia o reseñas.</li>
          <li>Usar el sitio para engañar, acosar o enviar publicidad no pedida a quien aparece en él.</li>
          <li>Intentar entrar donde no corresponde, sobrecargar el servicio o extraer datos de forma masiva y automática.</li>
          <li>Publicar contenido ilegal, discriminatorio o que afecte la privacidad de terceros.</li>
        </ul>
        <p>
          Si alguien incumple, podemos despublicar su ficha o suspender su cuenta. Si ves algo que no
          corresponde, escríbenos a <DatoLegal valor={CORREO_PRIVACIDAD} comoCorreo />.
        </p>

        <h2>6. Responsabilidad</h2>
        <p>
          Lo que acuerdes con quien contactes (el trabajo, el precio, los plazos, la garantía) es entre ustedes.
          ZASS! no controla ni garantiza la calidad, la seguridad ni el resultado de los servicios, y la
          información de cada ficha la escribe quien la publica. Hacemos lo razonable para que el sitio funcione y
          esté seguro, pero puede tener interrupciones. Esta sección es una de las que debe revisar un abogado
          antes de la versión definitiva.
        </p>

        <h2>7. Cambios</h2>
        <p>
          Si cambiamos estos términos, publicaremos la versión nueva aquí con su fecha. Si el cambio te afecta de
          forma importante, te avisaremos por correo o dentro del sitio.
        </p>

        <h2>8. Ley aplicable y contacto</h2>
        <p>
          Estos términos se rigen por las leyes de Chile. Para cualquier duda sobre ellos, o sobre tus datos,
          escribe a <DatoLegal valor={CORREO_PRIVACIDAD} comoCorreo />. Cómo cuidamos tus datos está en la{' '}
          <Link to="/privacidad">Política de privacidad</Link>.
        </p>
      </div>
    </article>
  )
}
