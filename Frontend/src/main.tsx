import { LanguageProvider } from './LanguageProvider.tsx'
import { StartupError } from './StartupError.tsx'
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
    root.render(<StrictMode><LanguageProvider><ApiProvider api={api}><App /></ApiProvider></LanguageProvider></StrictMode>)
  } catch {
    root.render(<LanguageProvider><StartupError /></LanguageProvider>)
  }
}
void start()
