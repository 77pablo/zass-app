import { Link } from 'react-router-dom'
import { Icono } from '../marco/Icono'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'

/**
 * La invitacion a ofrecer un servicio. Lo que promete es lo que la aplicacion ya hace hoy:
 * armar la ficha, elegir barrios y aparecer en la busqueda. Nada de sellos revisados ni de
 * agenda: eso no existe todavia y no se anuncia.
 */
export function Profesionales() {
  useTitulo('Para profesionales')
  useMeta({ descripcion: 'Ofrece tu servicio en ZASS!: crea tu ficha, aparece ante quien te busca cerca y paga una mensualidad. Buscar es gratis para tus clientes.' })
  return (
    <div className="pagina">
      <section className="pagina-cabeza pagina-cabeza--tinta">
        <div className="contenedor">
          <p className="sobretitulo">Para profesionales</p>
          <h1 className="pagina-titulo">Que te encuentre el que vive a cuadras de ti.</h1>
          <p className="bajada">
            Arma tu ficha en unos minutos: quién eres, qué ofreces y en qué barrios atiendes. Quien
            busca en tu zona te ve, y te escribe directo.
          </p>
          <p className="seccion-cierre">
            <Link className="boton boton--amarillo" to="/crear-cuenta?rol=prestador">
              Crear mi ficha<Icono nombre="flecha" />
            </Link>
          </p>
        </div>
      </section>

      <section className="seccion">
        <div className="contenedor">
          <h2 className="seccion-titulo">Así empiezas</h2>
          <ol className="pasos">
            <li>
              <span className="paso-numero" aria-hidden="true">1</span>
              <h3>Crea tu cuenta de profesional</h3>
              <p>Con tu correo y una contraseña. Confirmas tu correo y listo.</p>
            </li>
            <li>
              <span className="paso-numero" aria-hidden="true">2</span>
              <h3>Arma tu ficha</h3>
              <p>
                Tu presentación, tu oficio, tus servicios con duración y precio de referencia, y
                los barrios donde atiendes.
              </p>
            </li>
            <li>
              <span className="paso-numero" aria-hidden="true">3</span>
              <h3>Publícala</h3>
              <p>
                Al publicar empieza tu período de prueba y apareces en las búsquedas de tus
                barrios. Puedes pausarla cuando quieras.
              </p>
            </li>
          </ol>
        </div>
      </section>

      <section className="seccion oscuro sobre-tinta">
        <div className="contenedor">
          <h2 className="seccion-titulo">Tú decides qué se ve.</h2>
          <div className="principios">
            <div className="principio">
              <Icono nombre="pin" />
              <h3>Tu dirección no se publica</h3>
              <p>Se muestra tu barrio y una distancia redondeada. Nadie ve dónde vives.</p>
            </div>
            <div className="principio">
              <Icono nombre="mensaje" />
              <h3>Contacto directo</h3>
              <p>
                Publicas el WhatsApp, el teléfono o el correo que quieras, y quien te busca te
                escribe a ti.
              </p>
            </div>
            <div className="principio">
              <Icono nombre="persona" />
              <h3>Buscar es gratis</h3>
              <p>Quien te busca no paga nunca. La mensualidad la pagas tú, por estar visible.</p>
            </div>
          </div>
        </div>
      </section>
    </div>
  )
}
