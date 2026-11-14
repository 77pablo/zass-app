import { Link } from 'react-router-dom'
import { Icono } from '../marco/Icono'
import { useMeta } from '../marco/useMeta'
import { useTitulo } from '../marco/useTitulo'

/** Una URL que no lleva a ningun lado. Dice que paso y da por donde seguir. */
export function NoEncontrada({
  titulo = 'Esta página no existe.',
  texto = 'Puede que el enlace esté mal escrito o que ya no esté. Desde aquí puedes seguir.',
}: { titulo?: string; texto?: string }) {
  useTitulo('No encontrada')
  useMeta({ noindex: true })
  return (
    <section className="contenedor no-encontrada">
      <p className="sobretitulo sobretitulo--tinta">Error 404</p>
      <h1 className="pagina-titulo">{titulo}</h1>
      <p className="bajada">{texto}</p>
      <p className="seccion-cierre">
        <Link className="boton boton--amarillo" to="/buscar">Buscar a alguien<Icono nombre="flecha" /></Link>
        <Link className="boton" to="/oficios">Ver oficios</Link>
        <Link className="boton" to="/comunas">Ver comunas</Link>
        <Link className="boton" to="/">Ir al inicio</Link>
      </p>
    </section>
  )
}
