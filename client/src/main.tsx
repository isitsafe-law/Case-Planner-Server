import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { EntraProvider, initializeEntra, loadAuthConfiguration } from './auth.tsx'

const root = createRoot(document.getElementById('root')!)

async function start() {
  const config = await loadAuthConfiguration()
  if (!config.enabled) {
    root.render(<StrictMode><App /></StrictMode>)
    return
  }

  const instance = await initializeEntra(config)
  root.render(<StrictMode><EntraProvider instance={instance}><App /></EntraProvider></StrictMode>)
}

void start().catch(error => {
  const message = error instanceof Error ? error.message : String(error)
  root.render(<main className="auth-loading">Authentication startup failed: {message}</main>)
})
