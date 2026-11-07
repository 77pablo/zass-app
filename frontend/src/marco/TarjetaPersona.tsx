import { Link } from 'react-router-dom'
import { Icono } from './Icono'
import { Monograma } from './Monograma'

export interface DatosDeTarjeta {
  slug: string
  nombre: string
  rubro: string | null
  zona: string | null
  verificada: boolean
  /** Texto corto extra: "a 3 km", "4,8 de 5 · 12 reseñas". */
  detalle?: string | null
  descripcion?: string | null
  esTuPerfil?: boolean
}

/**
 * Una persona de la comunidad. Toda la tarjeta es el enlace a su ficha: el objetivo es grande
 * y el lector de pantalla oye un solo enlace con el nombre, no cuatro.
 */
export function TarjetaPersona({ p }: { p: DatosDeTarjeta }) {
  return (
    <Link to={`/prestadores/${p.slug}`} className="tarjeta-persona">
      <div className="tarjeta-cabeza">
        <Monograma nombre={p.nombre} />
        <div>
          <h3 className="tarjeta-nombre">{p.nombre}</h3>
          {p.rubro && <p className="tarjeta-detalle">{p.rubro}</p>}
        </div>
      </div>

      <div className="fila-etiquetas">
        {p.verificada && (
          <span className="sello"><Icono nombre="sello" />Verificado</span>
        )}
        {p.esTuPerfil && <span className="dato">Este eres tú</span>}
        {p.zona && <span className="dato"><Icono nombre="pin" />{p.zona}</span>}
        {p.detalle && <span className="dato">{p.detalle}</span>}
      </div>

      {p.descripcion && <p className="tarjeta-texto">{p.descripcion}</p>}

      <span className="tarjeta-ver">
        Ver su ficha<Icono nombre="flecha" />
      </span>
    </Link>
  )
}
