import { useEffect, useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { ErrorApi } from '../api/cliente'
import { Aviso } from '../componentes/Aviso'
import { Campo } from '../componentes/Campo'
import { miPerfil, type EstadoPerfil } from '../funciones/perfil/servicio'
import {
  activarTarjeta, LLEGA_ACTIVADA, normalizarCodigo, segmentoValido,
} from '../funciones/tarjetas/servicio'
import * as T from '../funciones/tarjetas/textos'
import { Icono } from '../marco/Icono'
import { PaginaCargando } from '../marco/PaginaCargando'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'
import { useSesion } from '../sesion/contexto'
import { AvisoDeTarjeta } from './AvisoDeTarjeta'
import { TEXTOS_DE_TARJETA } from './textosDeTarjeta'

/**
 * CU-19 · /tarjeta/sin-activar/:segmento. A donde lleva API-54 cuando se escanea una tarjeta
 * emitida y sin dueño: el aviso de «sin activar» de siempre y, para quien la recibió, el
 * formulario para activarla con el código de un solo uso.
 *
 * El segmento viene en la dirección porque la API lo pone ahí: es el mismo que se acaba de
 * escanear, así que no dice nada que quien escaneó no supiera. Con uno que no tiene la forma de
 * uno impreso se muestra el aviso a secas, sin formulario.
 */
export function ActivarTarjeta() {
  useMeta({ noindex: true })
  const segmento = segmentoValido(useParams().segmento)
  if (segmento === null) return <AvisoDeTarjeta aviso="sin-activar" />
  return <Activacion segmento={segmento} />
}

function Activacion({ segmento }: { segmento: string }) {
  useTitulo(T.ACTIVAR_PESTANA)
  const { estado, usuario } = useSesion()
  const { pathname } = useLocation()
  const esPrestador = usuario?.roles.includes('prestador') ?? false

  // El perfil se mira ANTES de pedir el código (CU-19 4E): sin ficha publicada la API lo rechaza
  // igual, pero así la persona sabe qué le falta en vez de gastar un intento.
  const [perfil, setPerfil] = useState<EstadoPerfil | 'sin-dato' | null>(null)
  useEffect(() => {
    if (estado !== 'dentro' || !esPrestador) return
    let vigente = true
    miPerfil()
      .then((p) => { if (vigente) setPerfil(p.estado) })
      // Sin el dato se ofrece el formulario: la que decide es la API.
      .catch(() => { if (vigente) setPerfil('sin-dato') })
    return () => { vigente = false }
  }, [estado, esPrestador])

  if (estado === 'cargando' || (estado === 'dentro' && esPrestador && perfil === null)) {
    return <PaginaCargando titulo={T.ACTIVAR_PESTANA} />
  }

  const { titulo } = TEXTOS_DE_TARJETA['sin-activar']
  let cuerpo
  if (estado === 'fuera') {
    cuerpo = (
      <Link className="boton boton--tinta" to="/entrar" state={{ despues: pathname }}>
        {T.ENTRAR_PARA_ACTIVAR}<Icono nombre="flecha" />
      </Link>
    )
  } else if (!esPrestador) {
    cuerpo = (
      <>
        <p className="bajada">{T.NO_OFRECE_SERVICIOS}</p>
        <p className="bloque-texto">
          {T.NO_PRESTADOR_ANTES}
          <Link to="/ofrecer-servicios">{T.NO_PRESTADOR_ENLACE}</Link>
          {T.NO_PRESTADOR_DESPUES}
        </p>
      </>
    )
  } else if (perfil === 'Borrador' || perfil === 'Despublicado') {
    cuerpo = (
      <>
        <p className="bajada">{T.FICHA_SIN_PUBLICAR}</p>
        <Link className="boton boton--tinta" to="/mi-ficha">{T.IR_A_MI_FICHA}<Icono nombre="flecha" /></Link>
      </>
    )
  } else {
    cuerpo = <Formulario segmento={segmento} />
  }

  return (
    <div className="pagina-formulario">
      <div className="contenedor contenedor--angosto">
        <p className="sobretitulo sobretitulo--tinta">Tarjeta ZASS!</p>
        <div className="tarjeta">
          <h1 className="titulo">{titulo}</h1>
          <p className="bajada">{T.ACTIVAR_PARA_QUIEN}</p>
          {cuerpo}
        </div>
      </div>
    </div>
  )
}

/**
 * Qué decir cuando la API no activó. NO se mira el texto ni el cuerpo de la respuesta: todo 422
 * es el mismo «el código no es válido», como lo responde la API (CU-19 4B.3). El 429 es el límite
 * por IP y sí se distingue: no dice nada de esta tarjeta.
 */
function mensajeDeFallo(e: unknown): string {
  if (!(e instanceof ErrorApi)) return T.ERROR_ACTIVAR
  if (e.estado === 422) return T.CODIGO_NO_VALIDO
  if (e.estado === 429) return T.MUCHOS_INTENTOS
  return T.ERROR_ACTIVAR
}

function Formulario({ segmento }: { segmento: string }) {
  const navegar = useNavigate()
  const { recargar } = useSesion()
  const [codigo, setCodigo] = useState('')
  const [errorCampo, setErrorCampo] = useState<string>()
  const [aviso, setAviso] = useState<string>()
  const [enviando, setEnviando] = useState(false)

  async function alEnviar(e: FormEvent) {
    e.preventDefault()
    setAviso(undefined)
    const normal = normalizarCodigo(codigo)
    if (normal === null) { setErrorCampo(T.CODIGO_INCOMPLETO); return }
    setErrorCampo(undefined)

    setEnviando(true)
    try {
      await activarTarjeta(segmento, normal)
      navegar('/mis-tarjetas', { replace: true, state: { aviso: LLEGA_ACTIVADA } })
    } catch (e2) {
      setEnviando(false)
      // 401 después de intentar renovar: la sesión se cerró. Al recargarla, la pantalla pasa
      // sola a «Entra para activarla», que vuelve aquí.
      if (e2 instanceof ErrorApi && e2.estado === 401) { void recargar(); return }
      setAviso(mensajeDeFallo(e2))
    }
  }

  return (
    <form onSubmit={(e) => void alEnviar(e)} noValidate>
      {aviso && <Aviso>{aviso}</Aviso>}
      <Campo id="codigo-activacion" etiqueta={T.ETIQUETA_CODIGO} autoComplete="off" maximo={20}
        ayuda={T.AYUDA_CODIGO} valor={codigo} alCambiar={setCodigo} error={errorCampo} />
      <button className="boton boton--tinta" type="submit" disabled={enviando}>
        {enviando ? T.ACTIVANDO : T.ACTIVAR}
      </button>
      <p className="letra-chica">{T.REGLAS_DEL_CODIGO}</p>
    </form>
  )
}
