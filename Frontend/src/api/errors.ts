export type ApiErrorKind = 'http' | 'network' | 'timeout' | 'aborted' | 'response' | 'validation'

export class ApiError extends Error {
  readonly kind: ApiErrorKind
  readonly status?: number
  readonly traceId?: string
  readonly errors?: Record<string, string[]>

  constructor(kind: ApiErrorKind, message: string, details: {
    status?: number; traceId?: string; errors?: Record<string, string[]>
  } = {}) {
    super(message)
    this.name = 'ApiError'
    this.kind = kind
    this.status = details.status
    this.traceId = details.traceId
    this.errors = details.errors
  }
}

export function httpError(status: number, body: unknown): ApiError {
  const fallback = status === 400 ? 'Check the information you entered.'
    : status === 403 ? 'You cannot change this application.'
    : status === 404 ? 'The requested class or application was not found.'
    : status === 409 ? 'This action is no longer available. Refresh the application status.'
    : 'The request could not be completed. Please try again.'
  const problem = typeof body === 'object' && body !== null ? body as Record<string, unknown> : {}
  // Server failures keep a generic message; never surface HTML, raw bodies, or exception causes.
  const message = status < 500 && typeof problem.title === 'string' && problem.title.trim()
    ? problem.title : fallback
  const errors: Record<string, string[]> = {}
  if (status === 400 && typeof problem.errors === 'object' && problem.errors !== null) {
    for (const [field, messages] of Object.entries(problem.errors)) {
      if (Array.isArray(messages) && messages.every(message => typeof message === 'string')) errors[field] = messages
    }
  }
  return new ApiError('http', message, {
    status,
    traceId: typeof problem.traceId === 'string' ? problem.traceId : undefined,
    errors: Object.keys(errors).length ? errors : undefined,
  })
}
