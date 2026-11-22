import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { duracion, fechaYHora, indicadores, porcentaje, type Indicadores as Datos } from '../funciones/interno/servicio'
import * as T from '../funciones/interno/textos'
import { PaginaCargando } from '../marco/PaginaCargando'
import { useTitulo } from '../marco/useTitulo'

/**
 * JQ-14 · API-60. Los indicadores de la operación, como cifras (no gráficos, lo pide el plan).
 * Solo para `administrador`, igual que la API.
 */
export function Indicadores() {
  useTitulo(T.TITULO_INDICADORES)
  const [datos, setDatos] = useState<Datos | null>(null)
  const [error, setError] = useState(false)
  const [recarga, setRecarga] = useState(0)

  useEffect(() => {
    let vigente = true
    indicadores()
      .then((d) => { if (vigente) { setDatos(d); setError(false) } })
      .catch(() => { if (vigente) setError(true) })
    return () => { vigente = false }
  }, [recarga])

  if (error) {
    return (
      <div className="contenedor ficha-pagina">
        <h1 className="pagina-titulo">{T.TITULO_INDICADORES}</h1>
        <p className="aviso-linea" role="alert">
          {T.ERROR_INDICADORES}{' '}
          <button type="button" className="enlace" onClick={() => { setError(false); setRecarga((n) => n + 1) }}>
            {T.REINTENTAR}
          </button>
        </p>
      </div>
    )
  }
  if (datos === null) return <PaginaCargando titulo={T.TITULO_INDICADORES} />

  const v = datos.verificacion
  return (
    <div className="contenedor ficha-pagina mesa-verificacion">
      <h1 className="pagina-titulo">{T.TITULO_INDICADORES}</h1>
      <p className="bloque-texto"><Link className="enlace" to="/interno">{T.VOLVER_AL_PANEL}</Link></p>
      <p className="bloque-texto letra-chica">{T.CALCULADO_EL} {fechaYHora(datos.calculadoEn)}</p>

      <section className="bloque" aria-labelledby="ind-verificacion">
        <h2 id="ind-verificacion">{T.GRUPO_VERIFICACION}</h2>
        <dl className="cifras">
          <Cifra etiqueta={T.IND_EN_COLA} valor={String(v.enCola)} />
          {v.masAntiguaDesde && <Cifra etiqueta={T.IND_MAS_ANTIGUA} valor={fechaYHora(v.masAntiguaDesde)} />}
          <Cifra
            etiqueta={T.IND_TASA}
            valor={v.tasaDeRechazo === null ? T.SIN_DATOS_AUN : porcentaje(v.tasaDeRechazo)}
            nota={v.tasaDeRechazo === null ? undefined : T.indResueltas(v.rechazadas, v.resueltas)}
          />
          <Cifra
            etiqueta={T.IND_TIEMPO}
            valor={v.horasPromedioDesdeCreacion === null ? T.SIN_DATOS_AUN : duracion(v.horasPromedioDesdeCreacion)}
            nota={T.IND_TIEMPO_NOTA}
          />
        </dl>
      </section>

      <section className="bloque" aria-labelledby="ind-operacion">
        <h2 id="ind-operacion">{T.GRUPO_OPERACION}</h2>
        <dl className="cifras">
          <Cifra etiqueta={T.IND_FALLIDOS} valor={String(datos.avisos.correosFallidos)} />
          <Cifra etiqueta={T.IND_REPORTES} valor={String(datos.reportesAbiertos)} />
          <Cifra etiqueta={T.IND_TARJETAS} valor={String(datos.tarjetasEmitidasSinActivar)} />
        </dl>
      </section>
    </div>
  )
}

function Cifra({ etiqueta, valor, nota }: { etiqueta: string; valor: string; nota?: string }) {
  return (
    <div className="cifra">
      <dt>{etiqueta}</dt>
      <dd className="cifra-valor">{valor}</dd>
      {nota && <dd className="letra-chica">{nota}</dd>}
    </div>
  )
}
