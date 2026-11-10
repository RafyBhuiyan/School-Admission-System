import type { ReactNode } from 'react'
import type { AdmissionApi } from './api/client.ts'
import { ApiContext } from './apiContext.ts'

export function ApiProvider({ api, children }: { api: AdmissionApi; children: ReactNode }) {
  return <ApiContext value={api}>{children}</ApiContext>
}
