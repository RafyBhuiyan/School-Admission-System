export interface AppConfig {
  apiBaseUrl: string
}

export function parseConfig(value: unknown): AppConfig {
  const raw = typeof value === 'object' && value !== null && 'API_BASE_URL' in value
    ? value.API_BASE_URL : undefined
  if (typeof raw !== 'string' || !raw.trim()) throw new Error('Invalid application configuration.')
  const url = new URL(raw.trim())
  if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password || url.search || url.hash) {
    throw new Error('Invalid application configuration.')
  }
  return { apiBaseUrl: url.href.replace(/\/+$/, '') }
}

export async function loadConfig(path: string, fetcher: typeof fetch = globalThis.fetch): Promise<AppConfig> {
  const response = await fetcher(path, {
    cache: 'no-store', credentials: 'omit', redirect: 'error', signal: AbortSignal.timeout(10_000),
  })
  if (!response.ok) throw new Error('Application configuration could not be loaded.')
  return parseConfig(await response.json())
}
