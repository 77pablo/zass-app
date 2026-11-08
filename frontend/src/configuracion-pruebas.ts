import '@testing-library/jest-dom/vitest'

// Las pruebas de las Pages Functions corren en entorno `node` (no hay window); las de interfaz, en jsdom.
if (typeof window !== 'undefined') {
  Object.defineProperty(window, 'scrollTo', { value: () => {}, writable: true })
}
