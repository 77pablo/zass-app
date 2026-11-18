import { Link } from 'react-router-dom'
import { Icono } from './Icono'
import { Monograma } from './Monograma'
import { pesos } from './texto'

export interface DatosDeTarjeta {
  slug: string
  nombre: string
  rubro: string | null
  zona: string | null
  verificada: boolean
  /** Texto corto extra: "4,8 de 5 · 12 reseñas". */
  detalle?: string | null
  /** "a 3 km": solo si la persona dio su ubicacion. */
  distancia?: string | null
  /** El servicio mas barato que ofrece, en pesos. */
  precioDesde?: number | null
  descripcion?: string | null
  esTuPerfil?: boolean
}

/**
 * Una persona de la comunidad. Toda la tarjeta es el enlace a su ficha: el objetivo es grande y el lector
 * de pantalla oye un solo enlace con el nombre, no cinco.
 * (Tenia un foco de luz que seguia al puntero; la guia v3 lo quito: era adorno «tecnologico».)
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
        {p.verificada && <span className="sello"><Icono nombre="sello" />Verificado</span>}
        {p.esTuPerfil && <span className="dato">Este eres tú</span>}
        {/* La comuna va siempre, y primero: en Chile se decide por ella («atiende en Temuco»).
            Los kilometros la acompañan cuando la persona compartio su ubicacion, no la reemplazan. */}
        {p.zona && <span className="dato"><Icono nombre="pin" />{p.zona}</span>}
        {p.distancia && <span className="dato dato--mono">{p.distancia}</span>}
        {p.detalle && <span className="dato">{p.detalle}</span>}
      </div>

      {p.descripcion && <p className="tarjeta-texto">{p.descripcion}</p>}

      <span className="tarjeta-ver">
        {p.precioDesde != null
          ? <span className="tarjeta-precio">desde <strong>{pesos(p.precioDesde)}</strong></span>
          : <span className="tarjeta-precio tarjeta-precio--vacio">Precio a convenir</span>}
        <span className="tarjeta-ir">Ver ficha<Icono nombre="flecha" /></span>
      </span>
    </Link>
  )
}
