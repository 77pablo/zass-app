import { useEffect, useId, useRef, useState, type FormEvent, type KeyboardEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useSugerencias } from '../api/useSugerencias'
import { Icono } from './Icono'
import { Monograma } from './Monograma'
import { EJEMPLOS, personas } from './texto'

interface Opcion {
  clave: string
  etiqueta: string
  detalle?: string
  nombrePersona?: string
  verificada?: boolean
  alElegir: () => void
}

/**
 * La barra de busqueda, como la de Google o la de Instagram: se escribe lo que se busca (un oficio, un
 * servicio o un nombre) y mientras se escribe aparecen personas; Enter lleva a todos los resultados.
 *
 * Es un «combobox» con su lista: lo que se oye con lector de pantalla es el numero de coincidencias y la
 * opcion activa, y se maneja con flechas, Enter y Escape sin tocar el raton.
 */
export function BarraDeBusqueda({
  valorInicial = '', variante = 'pagina', alEnviar,
}: {
  valorInicial?: string
  variante?: 'portada' | 'pagina'
  /** Si el texto cambia desde fuera (atras en el historial), quien la usa le pone otra `key`. */
  /** Si no se da, la barra navega a /buscar?q=... por su cuenta. */
  alEnviar?: (texto: string) => void
}) {
  const navegar = useNavigate()
  const id = useId()
  const idLista = `${id}-lista`
  const contenedor = useRef<HTMLDivElement>(null)

  const [texto, setTexto] = useState(valorInicial)
  const [abierto, setAbierto] = useState(false)
  const [activo, setActivo] = useState(-1)
  const [ejemplo, setEjemplo] = useState(0)

  // El ejemplo del campo vacio rota despacio: dice que se puede buscar de todo sin ocupar espacio.
  useEffect(() => {
    if (texto !== '') return
    const t = setInterval(() => setEjemplo((n) => (n + 1) % EJEMPLOS.length), 2600)
    return () => clearInterval(t)
  }, [texto])

  const sugerencias = useSugerencias(texto)
  const limpio = texto.trim()

  function enviar(q: string) {
    setAbierto(false)
    if (alEnviar) alEnviar(q)
    else navegar(q === '' ? '/buscar' : `/buscar?q=${encodeURIComponent(q)}`)
  }

  const opciones: Opcion[] = []
  if (limpio.length >= 2) {
    for (const p of sugerencias.personas) {
      opciones.push({
        clave: p.slug,
        etiqueta: p.nombrePublico,
        detalle: [p.rubro, p.zonaPrincipal].filter(Boolean).join(' · '),
        nombrePersona: p.nombrePublico,
        verificada: p.tieneCredencialVerificada,
        alElegir: () => { setAbierto(false); navegar(`/prestadores/${p.slug}`) },
      })
    }
    opciones.push({
      clave: '__todos',
      etiqueta: sugerencias.total > 0
        ? `Ver las ${personas(sugerencias.total)} para «${limpio}»`
        : `Buscar «${limpio}»`,
      alElegir: () => enviar(limpio),
    })
  } else {
    for (const e of EJEMPLOS.slice(0, 5)) {
      opciones.push({ clave: `ej-${e}`, etiqueta: e, detalle: 'Probar con este', alElegir: () => enviar(e) })
    }
  }

  function alTeclear(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setAbierto(true)
      setActivo((n) => (n + 1) % opciones.length)
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setAbierto(true)
      setActivo((n) => (n <= 0 ? opciones.length - 1 : n - 1))
    } else if (e.key === 'Escape') {
      setAbierto(false)
      setActivo(-1)
    } else if (e.key === 'Enter' && abierto && activo >= 0 && activo < opciones.length) {
      e.preventDefault()
      opciones[activo].alElegir()
    }
  }

  function alEnviarFormulario(e: FormEvent) {
    e.preventDefault()
    enviar(limpio)
  }

  const mensajeEstado = limpio.length < 2 ? '' : sugerencias.cargando ? 'Buscando…'
    : sugerencias.error ? 'No pudimos buscar ahora.'
      : sugerencias.total === 0 ? 'Sin coincidencias todavía.' : `${personas(sugerencias.total)} encontradas.`

  return (
    <div
      ref={contenedor} className={`barra barra--${variante}`}
      onBlur={(e) => { if (!contenedor.current?.contains(e.relatedTarget)) { setAbierto(false); setActivo(-1) } }}
    >
      <form role="search" className="barra-forma" onSubmit={alEnviarFormulario}>
        <label className="solo-lector" htmlFor={id}>Buscar un oficio, un servicio o a una persona</label>
        <Icono nombre="lupa" className="barra-icono" />
        <input
          id={id} className="barra-campo" type="search" autoComplete="off" spellCheck={false}
          role="combobox" aria-expanded={abierto} aria-controls={idLista} aria-autocomplete="list"
          aria-activedescendant={abierto && activo >= 0 ? `${id}-op-${activo}` : undefined}
          maxLength={80} value={texto}
          placeholder={`Prueba con «${EJEMPLOS[ejemplo]}»`}
          onChange={(e) => { setTexto(e.target.value); setAbierto(true); setActivo(-1) }}
          onFocus={() => setAbierto(true)} onKeyDown={alTeclear}
        />
        {texto !== '' && (
          <button type="button" className="barra-limpiar" aria-label="Borrar lo escrito"
            onClick={() => { setTexto(''); setActivo(-1); contenedor.current?.querySelector('input')?.focus() }}>
            <Icono nombre="cerrar" />
          </button>
        )}
        <button type="submit" className="barra-boton">Buscar<Icono nombre="flecha" /></button>
      </form>

      <p className="solo-lector" role="status" aria-live="polite">{abierto ? mensajeEstado : ''}</p>

      {abierto && (
        <ul id={idLista} role="listbox" className="barra-lista" aria-label="Sugerencias">
          {limpio.length >= 2 && sugerencias.cargando && opciones.length === 1 && (
            <li className="barra-estado" aria-hidden="true">Buscando…</li>
          )}
          {opciones.map((o, i) => (
            <li
              key={o.clave} id={`${id}-op-${i}`} role="option" aria-selected={i === activo}
              className={`barra-opcion${i === activo ? ' barra-opcion--activa' : ''}${o.clave === '__todos' ? ' barra-opcion--todos' : ''}`}
              // mouseDown y no click: el click llegaria despues de que el campo pierde el foco y la lista se cierra.
              onMouseDown={(e) => { e.preventDefault(); o.alElegir() }}
              onMouseEnter={() => setActivo(i)}
            >
              {o.nombrePersona ? <Monograma nombre={o.nombrePersona} tamano="p" /> : <Icono nombre={o.clave === '__todos' ? 'flecha' : 'lupa'} />}
              <span className="barra-opcion-texto">
                <strong>{o.etiqueta}</strong>
                {o.detalle && <small>{o.detalle}</small>}
              </span>
              {o.verificada && <span className="sello"><Icono nombre="sello" />Verificado</span>}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
