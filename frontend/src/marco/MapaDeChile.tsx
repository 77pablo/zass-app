import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import type { Zona } from '../api/catalogo'
import { ALTO, ANCHO, proyectarChile } from './proyeccionChile'
import { personas } from './texto'

/**
 * Chile hecho de puntos: cada comuna es un punto en su lugar real, y los que tienen gente brillan y laten.
 * Tocar uno lleva a la busqueda de esa comuna. Las posiciones salen de los centros de las comunas (dato
 * publico, OpenStreetMap), no de un dibujo.
 */
export function MapaDeChile({
  zonas, conteo,
}: { zonas: Zona[]; conteo: (zonaId: number) => number }) {
  const [activa, setActiva] = useState<number | null>(null)

  const puntos = useMemo(() => proyectarChile(zonas, conteo), [zonas, conteo])
  const vivos = puntos.filter((p) => p.n > 0)
  const quietos = puntos.filter((p) => p.n === 0)
  const elegida = vivos.find((p) => p.id === activa) ?? null
  const mayor = [...vivos].sort((a, b) => b.n - a.n)[0]

  return (
    <figure className="mapa">
      <svg viewBox={`0 0 ${ANCHO} ${ALTO}`} role="group" aria-label="Mapa de Chile con las comunas que ya tienen gente">
        <defs>
          <linearGradient id="mapa-barrido" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor="#f6c81e" stopOpacity="0" />
            <stop offset="100%" stopColor="#f6c81e" stopOpacity="0.28" />
          </linearGradient>
        </defs>

        {/* El resto del pais, apagado: todas las comunas, para que se lea la forma de Chile */}
        <g aria-hidden="true">
          {quietos.map((p) => <circle key={p.id} cx={p.x} cy={p.y} r="1.9" className="mapa-punto" />)}
        </g>

        <rect x="0" y="-60" width={ANCHO} height="60" fill="url(#mapa-barrido)" className="mapa-barrido" aria-hidden="true" />

        {vivos.map((p) => {
          const radio = 5 + Math.sqrt(p.n) * 2
          return (
            <Link
              key={p.id} to={`/buscar?zona=${p.id}`}
              aria-label={`${p.nombre}, ${p.region}: ${personas(p.n)}. Ver quién hay.`}
              className={`mapa-vivo${activa === p.id ? ' mapa-vivo--activo' : ''}`}
              onMouseEnter={() => setActiva(p.id)} onMouseLeave={() => setActiva(null)}
              onFocus={() => setActiva(p.id)} onBlur={() => setActiva(null)}
            >
              {/* Area de toque de 44 px aunque el punto sea chico */}
              <circle cx={p.x} cy={p.y} r="24" className="mapa-toque" />
              <circle cx={p.x} cy={p.y} r={radio} className="mapa-onda" />
              <circle cx={p.x} cy={p.y} r={radio} className="mapa-nucleo" />
              {(p === mayor || activa === p.id) && (
                <text x={p.x + radio + 8} y={p.y + 4} className="mapa-nombre">{p.nombre}</text>
              )}
            </Link>
          )
        })}
      </svg>

      <figcaption className="mapa-leyenda" aria-live="polite">
        {elegida
          ? <><strong>{elegida.nombre}</strong><span>{elegida.region} · {personas(elegida.n)}</span></>
          : <><strong>Chile, en vivo</strong><span>{vivos.length === 0 ? 'Pronto, tu comuna' : 'Toca un punto para ver quién hay'}</span></>}
      </figcaption>
    </figure>
  )
}
