import { createContext, useContext } from 'react'
import type { AdmissionApi } from './api/client.ts'

export const ApiContext = createContext<AdmissionApi | null>(null)

export function useApi(): AdmissionApi {
  const api = useContext(ApiContext)
  if (!api) throw new Error('The API provider is missing.')
  return api
}
