import { parseConfig } from '../config.ts'
import { ApiError, httpError } from './errors.ts'
import type { AdmissionClass, ClassSummary, ConfirmedHold, CreateClassRequest, CreatedHold, HoldDetails, StudentHold } from './types.ts'

export interface RequestOptions { signal?: AbortSignal }

export function createAdmissionApi(apiBaseUrl: string, options: { fetcher?: typeof fetch; timeoutMs?: number } = {}) {
  const base = parseConfig({ API_BASE_URL: apiBaseUrl }).apiBaseUrl
  const fetcher = options.fetcher ?? globalThis.fetch.bind(globalThis)
  const timeoutMs = options.timeoutMs ?? 15_000
  if (!Number.isInteger(timeoutMs) || timeoutMs <= 0) throw new Error('Invalid request timeout.')

  async function request<T>(path: string, method: string, requestOptions: RequestOptions = {}, studentId?: string, body?: CreateClassRequest): Promise<T> {
    const headers = new Headers({ Accept: 'application/json' })
    if (studentId !== undefined) {
      const value = studentId.trim()
      if (!value || value.length > 64 || /[\r\n]/.test(value)) {
        throw new ApiError('validation', 'Enter a valid student identifier.')
      }
      try { headers.set('X-Student-Id', value) }
      catch { throw new ApiError('validation', 'Enter a valid student identifier.') }
    }
    if (body !== undefined) headers.set('Content-Type', 'application/json')
    const timeout = AbortSignal.timeout(timeoutMs)
    const signal = requestOptions.signal ? AbortSignal.any([requestOptions.signal, timeout]) : timeout
    try {
      signal.throwIfAborted()
      const response = await fetcher(`${base}${path}`, {
        method, headers, body: body === undefined ? undefined : JSON.stringify(body), signal,
        credentials: 'omit', cache: 'no-store', redirect: 'error',
      })
      if (!response.ok) {
        const problem: unknown = await response.json().catch(() => undefined)
        throw httpError(response.status, problem)
      }
      if (response.status === 204) return undefined as T
      try { return await response.json() as T }
      catch { throw new ApiError('response', 'The server returned an unreadable response. Please try again.') }
    } catch (error) {
      if (requestOptions.signal?.aborted) throw new ApiError('aborted', 'The request was cancelled.')
      if (timeout.aborted) throw new ApiError('timeout', 'The request timed out. Check the application status before trying again.')
      if (error instanceof ApiError) throw error
      throw new ApiError('network', 'Could not reach the server. Check your connection and try again.')
    }
  }

  function id(value: string): string {
    if (!/^[\da-f]{8}-[\da-f]{4}-[\da-f]{4}-[\da-f]{4}-[\da-f]{12}$/i.test(value)) {
      throw new ApiError('validation', 'The class or application identifier is invalid.')
    }
    return encodeURIComponent(value)
  }

  return {
    createClass: (body: CreateClassRequest, opts?: RequestOptions) => request<AdmissionClass>('/classes', 'POST', opts, undefined, body),
    getClasses: (opts?: RequestOptions) => request<ClassSummary[]>('/classes', 'GET', opts),
    getClass: (classId: string, opts?: RequestOptions) => request<ClassSummary>(`/classes/${id(classId)}`, 'GET', opts),
    createHold: (classId: string, studentId: string, opts?: RequestOptions) => request<CreatedHold>(`/classes/${id(classId)}/holds`, 'POST', opts, studentId),
    getHold: (holdId: string, opts?: RequestOptions) => request<HoldDetails>(`/holds/${id(holdId)}`, 'GET', opts),
    confirmHold: (holdId: string, studentId: string, opts?: RequestOptions) => request<ConfirmedHold>(`/holds/${id(holdId)}/confirm`, 'POST', opts, studentId),
    cancelHold: (holdId: string, studentId: string, opts?: RequestOptions) => request<void>(`/holds/${id(holdId)}`, 'DELETE', opts, studentId),
    getMyHolds: (studentId: string, opts?: RequestOptions) => request<StudentHold[]>('/me/holds', 'GET', opts, studentId),
  }
}

export type AdmissionApi = ReturnType<typeof createAdmissionApi>
