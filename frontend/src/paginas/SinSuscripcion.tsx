import { Link } from 'react-router-dom'
import { Icono } from '../marco/Icono'

/*
 * RN-S6-10. Lo que se ve en una ficha cuyo prestador dejo de pagar.
 *
 * La decision de Pablo (8-nov-2026) es la del medio entre dos extremos malos: apagar la ficha
 * deja a quien tiene la tarjeta NFC en la mano sin entender que paso, y dejarla entera le
 * conserva al impago lo que mas se nota del producto. Asi que la ficha se ve, y lo que se
 * apaga es contactar y agendar.
 *
 * Ningun texto de aqui dice que la persona "no pago": eso es entre ella y ZASS!, no algo que
 * se le cuente a un desconocido que escaneo su tarjeta. Por eso "por ahora" y nada mas.
 *
 * TEXTOS PROPUESTOS, pendientes de aprobacion de Pablo (CONTRIBUIR.md, "textos visibles").
 */

/** En el lugar donde iria el calendario de CU-12. */
export function SinHorasPorAhora({ nombre }: { nombre: string }) {
  return (
    <section className="bloque agenda" aria-labelledby="agenda-titulo">
      <h2 id="agenda-titulo">Agendar una hora con {nombre.split(' ')[0]}</h2>
      <p className="bloque-texto bloque-vacio">
        {nombre.split(' ')[0]} no está tomando horas por ahora. Su ficha sigue aquí, pero la
        agenda está cerrada.
      </p>
    </section>
  )
}

/** En el lugar donde irian el WhatsApp, el telefono, el correo y el sitio. */
export function SinContactoPorAhora({ nombre }: { nombre: string }) {
  const primerNombre = nombre.split(' ')[0]
  return (
    <div className="contacto-cuenta" role="note">
      <p>
        {primerNombre} no está recibiendo contactos por ahora. Si llegaste por su tarjeta, no
        la pierdas: cuando vuelva, la misma tarjeta seguirá abriendo esta ficha.
      </p>
      <Link className="boton boton--amarillo boton--lleno" to="/buscar">
        <Icono nombre="flecha" />Buscar a alguien más
      </Link>
    </div>
  )
}
