/**
 * Iconos de linea, en un solo trazo para que se lean como una familia. Se pintan con
 * `currentColor`: toman el color del texto donde esten. Son decorativos (aria-hidden); el
 * significado lo lleva siempre el texto de al lado.
 */
const TRAZOS = {
  lupa: 'M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16zM21 21l-4.3-4.3',
  pin: 'M12 21s7-6.2 7-11.5A7 7 0 0 0 5 9.5C5 14.8 12 21 12 21zM12 12a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5z',
  sello: 'M12 3l7 3v5c0 4.5-3 8.2-7 10-4-1.8-7-5.5-7-10V6l7-3zM8.5 12l2.5 2.5L16 9.5',
  flecha: 'M5 12h14M13 6l6 6-6 6',
  check: 'M5 12.5l4.5 4.5L19 7.5',
  mas: 'M12 5v14M5 12h14',
  menu: 'M4 7h16M4 12h16M4 17h16',
  cerrar: 'M6 6l12 12M18 6L6 18',
  usuario: 'M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM4 21c0-4 3.6-6.5 8-6.5s8 2.5 8 6.5',
  estrella: 'M12 3.5l2.6 5.4 5.9.8-4.3 4.1 1 5.9L12 16.9 6.8 19.7l1-5.9L3.5 9.7l5.9-.8L12 3.5z',
  lapiz: 'M4 20l1-4L16.5 4.5a2.1 2.1 0 0 1 3 3L8 19l-4 1zM14.5 6.5l3 3',
  telefono: 'M5 4h4l2 5-2.5 1.5a11 11 0 0 0 5 5L15 13l5 2v4a2 2 0 0 1-2 2A16 16 0 0 1 3 6a2 2 0 0 1 2-2z',
  correo: 'M4 6h16v12H4zM4 7l8 6 8-6',
  mundo: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM3 12h18M12 3c2.5 2.7 3.5 5.7 3.5 9s-1 6.3-3.5 9c-2.5-2.7-3.5-5.7-3.5-9s1-6.3 3.5-9z',
  mensaje: 'M5 5h14a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1h-8l-5 4v-4H5a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1z',
  persona: 'M12 11a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7zM5 20c0-3.5 3-5.5 7-5.5s7 2 7 5.5',
  llave: 'M14 10a4 4 0 1 1 3.5 4L12 19.5H9V16.5L7.5 15 10 12.5A4 4 0 0 1 14 10z',
} as const

export type NombreDeIcono = keyof typeof TRAZOS

export function Icono({ nombre, className = '' }: { nombre: NombreDeIcono; className?: string }) {
  return (
    <svg
      className={`icono ${className}`.trim()} viewBox="0 0 24 24" aria-hidden="true" focusable="false"
    >
      <path d={TRAZOS[nombre]} />
    </svg>
  )
}
