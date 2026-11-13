import { useId, useState } from 'react'
import { SelectorDeEstrellas } from './Estrellas'
import { queFalta, type Borrador } from '../../funciones/resenas/borrador'
import {
  ASPECTOS, COMENTARIO_MAXIMO, COMENTARIO_MINIMO, largo, significado,
  type AvisoDeDatosPersonales,
} from '../../funciones/resenas/servicio'

/**
 * CU-16 pasos 3 a 7. El mismo formulario sirve para publicar y para editar dentro de las 72 h
 * (CS-16): lo unico que cambia es quien lo guarda.
 *
 * El aviso de datos personales (4E) llega de FUERA, porque lo detecta el servidor: la pantalla
 * no adivina lo que ya sabe MOD-11.
 */
export function FormularioDeResena({
  borrador, alCambiar, alEnviar, enviando, aviso, fallo, etiquetaEnviar, alCancelar, alCorregir,
}: {
  borrador: Borrador
  alCambiar: (borrador: Borrador) => void
  /** `enviarIgual` es true cuando la persona ya vio el aviso de datos personales y sigue (4E.3). */
  alEnviar: (enviarIgual: boolean) => void
  enviando: boolean
  aviso: AvisoDeDatosPersonales | null
  fallo: string | null
  etiquetaEnviar: string
  alCancelar?: () => void
  /**
   * Se llama en cuanto se toca el comentario que el servidor señalo. El permiso de "enviar
   * igual" vale para EL TEXTO QUE SE AVISO: si se corrige, hay que volver a preguntar, o un
   * texto nuevo con otro telefono dentro se publicaria sin que nadie lo mire.
   */
  alCorregir?: () => void
}) {
  const idNota = useId()
  const idComentario = useId()
  // Lo que falta se dice al INTENTAR enviar, no mientras se escribe: un aviso en rojo desde la
  // primera letra regaña a alguien que todavia no ha terminado.
  const [reclamo, setReclamo] = useState<string | null>(null)

  const escrito = largo(borrador.comentario.trim())
  const pasado = escrito > COMENTARIO_MAXIMO

  function enviar() {
    const falta = queFalta(borrador)
    setReclamo(falta)
    if (falta === null) alEnviar(aviso !== null)
  }

  return (
    <div className="resena-formulario">
      <p className="campo-titulo" id={idNota}>¿Cómo te fue?</p>
      <SelectorDeEstrellas
        valor={borrador.calificacion} idEtiqueta={idNota}
        alElegir={(calificacion) => { alCambiar({ ...borrador, calificacion }); setReclamo(null) }}
      />
      {borrador.calificacion !== null && (
        <p className="resena-significado">{significado(borrador.calificacion)}</p>
      )}

      <div className="campo-nota">
        <label htmlFor={idComentario}><span>Cuenta cómo fue el servicio</span></label>
        <textarea
          id={idComentario} rows={6} value={borrador.comentario}
          aria-describedby={`${idComentario}-cuenta`}
          onChange={(e) => {
            alCambiar({ ...borrador, comentario: e.target.value })
            setReclamo(null)
            if (aviso !== null) alCorregir?.()
          }}
        />
        <p className={pasado ? 'resena-cuenta resena-cuenta--pasada' : 'resena-cuenta'} id={`${idComentario}-cuenta`}>
          {escrito} de {COMENTARIO_MAXIMO} caracteres
          {escrito < COMENTARIO_MINIMO && ` · mínimo ${COMENTARIO_MINIMO}`}
        </p>
      </div>

      <fieldset className="resena-aspectos">
        <legend>¿Qué destacarías? (opcional)</legend>
        {ASPECTOS.map((a) => {
          const marcado = borrador.aspectos.includes(a.valor)
          return (
            <label key={a.valor} className={marcado ? 'resena-aspecto resena-aspecto--activo' : 'resena-aspecto'}>
              <input
                type="checkbox" checked={marcado}
                onChange={() => alCambiar({
                  ...borrador,
                  aspectos: marcado
                    ? borrador.aspectos.filter((x) => x !== a.valor)
                    : [...borrador.aspectos, a.valor],
                })}
              />
              <span>{a.etiqueta}</span>
            </label>
          )
        })}
        <p className="letra-chica">Las etiquetas no cambian el promedio: solo cuentan qué destacar.</p>
      </fieldset>

      {aviso !== null && (
        <div className="resena-datos-personales" role="alert">
          <p><strong>{aviso.mensaje}</strong></p>
          {aviso.fragmentos.length > 0 && (
            <ul className="resena-fragmentos">
              {aviso.fragmentos.map((f) => <li key={f}><code>{f}</code></li>)}
            </ul>
          )}
          <p className="letra-chica">
            Publicar el teléfono, el RUT o la dirección de otra persona puede meterte en problemas.
            Si lo envías igual, un moderador la revisará.
          </p>
        </div>
      )}

      {reclamo !== null && <p className="aviso-linea" role="alert">{reclamo}</p>}
      {fallo !== null && <p className="aviso-linea" role="alert">{fallo}</p>}

      <div className="resena-acciones">
        <button
          type="button" className="boton boton--amarillo boton--lleno" disabled={enviando}
          onClick={enviar}
        >
          {enviando ? 'Enviando…' : aviso !== null ? 'Enviar igual' : etiquetaEnviar}
        </button>
        {aviso !== null && (
          <span className="letra-chica">o corrige el texto y vuelve a enviar</span>
        )}
        {alCancelar !== undefined && (
          <button type="button" className="boton boton--chico" onClick={alCancelar}>Volver</button>
        )}
      </div>
    </div>
  )
}
