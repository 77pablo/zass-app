import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import type { Zona } from '../api/catalogo'
import { CENTRO, LADO, proyectarBarrios } from './proyeccion'
import { personas } from './texto'

/**
 * Los barrios de Temuco como un radar: cada punto es un barrio, crece con la gente que hay y late si hay
 * alguien. Las direcciones salen de los centros reales de las zonas (dato publico), y cada punto es un enlace
 * a la busqueda de ese barrio.
 */
export function Radar({
  zonas, conteo,
}: { zonas: Zona[]; conteo: (zonaId: number) => number }) {
  const [activa, setActiva] = useState<number | null>(null)

  const puntos = useMemo(() => proyectarBarrios(zonas, conteo), [zonas, conteo])
  const elegida = puntos.find((p) => p.id === activa) ?? null
  const mayores = new Set([...puntos].sort((a, b) => b.n - a.n).slice(0, 1).filter((p) => p.n > 0).map((p) => p.id))

  return (
    <figure className="radar">
      <svg viewBox={`0 0 ${LADO} ${LADO}`} role="group" aria-label="Mapa de los barrios de Temuco">
        <defs>
          <radialGradient id="radar-fondo" cx="50%" cy="50%" r="50%">
            <stop offset="0%" stopColor="#f6c81e" stopOpacity="0.14" />
            <stop offset="100%" stopColor="#f6c81e" stopOpacity="0" />
          </radialGradient>
          <linearGradient id="radar-barrido" x1="0" y1="0" x2="1" y2="0">
            <stop offset="0%" stopColor="#f6c81e" stopOpacity="0" />
            <stop offset="100%" stopColor="#f6c81e" stopOpacity="0.34" />
          </linearGradient>
        </defs>

        <circle cx={CENTRO} cy={CENTRO} r={LADO / 2 - 4} fill="url(#radar-fondo)" />
        {[75, 145, 215].map((r) => (
          <circle key={r} cx={CENTRO} cy={CENTRO} r={r} className="radar-anillo" />
        ))}
        <line x1={CENTRO} y1="18" x2={CENTRO} y2={LADO - 18} className="radar-eje" />
        <line x1="18" y1={CENTRO} x2={LADO - 18} y2={CENTRO} className="radar-eje" />

        <g className="radar-barrido" style={{ transformOrigin: `${CENTRO}px ${CENTRO}px` }} aria-hidden="true">
          <path
            d={`M ${CENTRO} ${CENTRO} L ${CENTRO + 235} ${CENTRO} A 235 235 0 0 0 ${CENTRO + 235 * Math.cos(Math.PI / 3.2)} ${CENTRO - 235 * Math.sin(Math.PI / 3.2)} Z`}
            fill="url(#radar-barrido)"
          />
        </g>

        {puntos.map((p) => {
          const radio = p.n > 0 ? 7 + Math.sqrt(p.n) * 2.4 : 4.5
          return (
            <Link
              key={p.id} to={`/buscar?zona=${p.id}`}
              aria-label={`${p.nombre}: ${p.n === 0 ? 'aún nadie' : personas(p.n)}. Ver quién hay.`}
              className={`radar-punto${p.n > 0 ? ' radar-punto--vivo' : ''}${activa === p.id ? ' radar-punto--activo' : ''}`}
              onMouseEnter={() => setActiva(p.id)} onMouseLeave={() => setActiva(null)}
              onFocus={() => setActiva(p.id)} onBlur={() => setActiva(null)}
            >
              {/* Un area de toque de 44 px aunque el punto sea chico y el mapa se reduzca en el celular */}
              <circle cx={p.x} cy={p.y} r="36" className="radar-toque" />
              {p.n > 0 && <circle cx={p.x} cy={p.y} r={radio} className="radar-onda" />}
              <circle cx={p.x} cy={p.y} r={radio} className="radar-nucleo" />
              <text x={p.x} y={p.y - radio - 9} textAnchor="middle" className={`radar-nombre${mayores.has(p.id) || activa === p.id ? ' radar-nombre--visible' : ''}`}>
                {p.nombre}
              </text>
            </Link>
          )
        })}
      </svg>

      <figcaption className="radar-leyenda" aria-live="polite">
        {elegida
          ? <><strong>{elegida.nombre}</strong><span>{elegida.n === 0 ? 'Aún nadie' : personas(elegida.n)}</span></>
          : <><strong>Temuco, en vivo</strong><span>Toca un barrio para ver quién hay</span></>}
      </figcaption>
    </figure>
  )
}
