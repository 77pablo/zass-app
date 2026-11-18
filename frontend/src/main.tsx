import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { App } from './App'
import { registrarServiceWorker } from './registrarServiceWorker'
import { ProveedorDeSesion } from './sesion/Sesion'
import './index.css'

// Instalable y sin conexion (public/sw.js). Solo en produccion.
registrarServiceWorker()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <ProveedorDeSesion>
        <App />
      </ProveedorDeSesion>
    </BrowserRouter>
  </StrictMode>,
)
