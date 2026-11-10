import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { ApiProvider } from './ApiProvider.tsx'
import { createAdmissionApi } from './api/client.ts'
import { loadConfig } from './config.ts'

const root = createRoot(document.getElementById('root')!)
async function start() {
  try {
    const config = await loadConfig(`${import.meta.env.BASE_URL}config.json`)
    const api = createAdmissionApi(config.apiBaseUrl)
    root.render(<StrictMode><ApiProvider api={api}><App /></ApiProvider></StrictMode>)
  } catch {
    root.render(<main role="alert"><h1>Unable to start</h1><p>Please refresh and try again. If the problem continues, contact the administrator.</p></main>)
  }
}
void start()
