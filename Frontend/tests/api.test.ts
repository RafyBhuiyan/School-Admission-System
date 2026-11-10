import assert from 'node:assert/strict'
import { createServer } from 'node:http'
import type { AddressInfo } from 'node:net'
import { test } from 'node:test'
import { createAdmissionApi } from '../src/api/client.ts'
import { ApiError } from '../src/api/errors.ts'
import { loadConfig } from '../src/config.ts'

const classId = '11111111-1111-1111-1111-111111111111'
const holdId = '22222222-2222-2222-2222-222222222222'

test('all eight endpoint methods use the expected paths, headers, methods, and bodies', async () => {
  const seen: { path: string; options: RequestInit }[] = []
  const fetcher: typeof fetch = async (path, options) => {
    seen.push({ path: String(path), options: options! })
    return options?.method === 'DELETE' ? new Response(null, { status: 204 }) : Response.json({ ok: true })
  }
  const api = createAdmissionApi('http://localhost:8080/api/', { fetcher })
  await api.createClass({ name: 'Morning', totalSeats: 10 })
  await api.getClasses()
  await api.getClass(classId)
  await api.createHold(classId, ' 000Student-1 ')
  await api.getHold(holdId)
  await api.confirmHold(holdId, ' 000Student-1 ')
  assert.equal(await api.cancelHold(holdId, ' 000Student-1 '), undefined)
  await api.getMyHolds(' 000Student-1 ')
  assert.deepEqual(seen.map(call => [call.options.method, call.path]), [
    ['POST', 'http://localhost:8080/api/classes'], ['GET', 'http://localhost:8080/api/classes'],
    ['GET', `http://localhost:8080/api/classes/${classId}`], ['POST', `http://localhost:8080/api/classes/${classId}/holds`],
    ['GET', `http://localhost:8080/api/holds/${holdId}`], ['POST', `http://localhost:8080/api/holds/${holdId}/confirm`],
    ['DELETE', `http://localhost:8080/api/holds/${holdId}`], ['GET', 'http://localhost:8080/api/me/holds'],
  ])
  for (const [index, call] of seen.entries()) {
    const headers = new Headers(call.options.headers)
    assert.equal(headers.get('X-Student-Id'), [3, 5, 6, 7].includes(index) ? '000Student-1' : null)
    assert.equal(headers.get('Content-Type'), index === 0 ? 'application/json' : null)
    assert.equal(headers.get('Accept'), 'application/json')
    assert.equal(call.options.credentials, 'omit')
    assert.equal(call.options.cache, 'no-store')
    assert.equal(call.options.redirect, 'error')
    assert.ok(call.options.signal)
    assert.ok(!call.path.includes('Student-1'))
  }
  assert.equal(seen[0].options.body, JSON.stringify({ name: 'Morning', totalSeats: 10 }))
  assert.ok(seen.slice(1).every(call => call.options.body === undefined))
})

for (const status of [400, 403, 404, 409, 500]) {
  test(`normalizes Problem Details for HTTP ${status}`, async () => {
    const api = createAdmissionApi('http://localhost:8080', { fetcher: async () => Response.json({
      title: 'A specific problem', detail: 'raw database exception', traceId: 'trace-123',
      errors: { Name: ['Enter a name'], malformed: 42 },
    }, { status }) })
    await assert.rejects(api.getClasses(), (error: unknown) => {
      assert.ok(error instanceof ApiError)
      assert.equal(error.kind, 'http')
      assert.equal(error.status, status)
      assert.equal(error.traceId, 'trace-123')
      assert.equal(error.message, status < 500 ? 'A specific problem' : 'The request could not be completed. Please try again.')
      assert.deepEqual(error.errors, status === 400 ? { Name: ['Enter a name'] } : undefined)
      assert.ok(!JSON.stringify(error).includes('raw database exception'))
      return true
    })
  })
  test(`uses a safe fallback for non-JSON HTTP ${status}`, async () => {
    const api = createAdmissionApi('http://localhost:8080', { fetcher: async () => new Response('<html>private failure</html>', { status }) })
    await assert.rejects(api.getClasses(), (error: unknown) => {
      assert.ok(error instanceof ApiError)
      assert.equal(error.status, status)
      assert.ok(!error.message.includes('private failure'))
      return true
    })
  })
}

test('malformed successful JSON is a response error', async () => {
  const api = createAdmissionApi('http://localhost:8080', { fetcher: async () => new Response('not json') })
  await assert.rejects(api.getClasses(), (error: unknown) => error instanceof ApiError && error.kind === 'response')
})

test('network failure is safe and a mutation is never automatically retried', async () => {
  let calls = 0
  const api = createAdmissionApi('http://localhost:8080', { fetcher: async () => { calls++; throw new Error('private connection detail') } })
  await assert.rejects(api.createHold(classId, 'student-1'), (error: unknown) => {
    assert.ok(error instanceof ApiError)
    assert.equal(error.kind, 'network')
    assert.ok(!String(error).includes('private connection detail'))
    return true
  })
  assert.equal(calls, 1)
})

test('pre-cancelled requests make no network call', async () => {
  const controller = new AbortController()
  controller.abort()
  let calls = 0
  const api = createAdmissionApi('http://localhost:8080', { fetcher: async () => { calls++; return Response.json([]) } })
  await assert.rejects(api.getClasses({ signal: controller.signal }), (error: unknown) => error instanceof ApiError && error.kind === 'aborted')
  assert.equal(calls, 0)
})

test('in-flight cancellation and timeout remain distinguishable', async () => {
  const fetcher: typeof fetch = async (_path, options) => new Promise((_resolve, reject) => {
    const keepAlive = setTimeout(() => reject(new Error('Abort did not reach fetch')), 1000)
    options!.signal!.addEventListener('abort', () => { clearTimeout(keepAlive); reject(options!.signal!.reason) }, { once: true })
  })
  const controller = new AbortController()
  const cancelled = createAdmissionApi('http://localhost:8080', { fetcher }).getClasses({ signal: controller.signal })
  controller.abort()
  await assert.rejects(cancelled, (error: unknown) => error instanceof ApiError && error.kind === 'aborted')
  await assert.rejects(createAdmissionApi('http://localhost:8080', { fetcher, timeoutMs: 10 }).getClasses(),
    (error: unknown) => error instanceof ApiError && error.kind === 'timeout')
})

test('invalid student headers and resource paths never reach fetch', async () => {
  let calls = 0
  const api = createAdmissionApi('http://localhost:8080', { fetcher: async () => { calls++; return Response.json([]) } })
  for (const student of ['', '   ', 'a'.repeat(65), 'student\r\ninjected']) {
    await assert.rejects(api.getMyHolds(student), (error: unknown) => error instanceof ApiError && error.kind === 'validation')
  }
  assert.throws(() => api.getClass('../me/holds'), ApiError)
  assert.throws(() => api.getHold('student-1'), ApiError)
  await api.getMyHolds(` ${'0'.repeat(64)} `)
  assert.equal(calls, 1)
})

test('a real HTTP server receives the runtime-selected URL and student header', async () => {
  let prefix = '/first'
  const seen: { path: string; student: string | string[] | undefined }[] = []
  const server = createServer((request, response) => {
    response.setHeader('Content-Type', 'application/json')
    const address = server.address() as AddressInfo
    if (request.url === '/config.json') {
      response.end(JSON.stringify({ API_BASE_URL: `http://127.0.0.1:${address.port}${prefix}` }))
    } else {
      seen.push({ path: request.url!, student: request.headers['x-student-id'] })
      response.end('[]')
    }
  })
  await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve))
  try {
    const url = `http://127.0.0.1:${(server.address() as AddressInfo).port}/config.json`
    const first = await loadConfig(url)
    await createAdmissionApi(first.apiBaseUrl).getMyHolds(' 000Student-1 ')
    prefix = '/changed-without-rebuild'
    const changed = await loadConfig(url)
    await createAdmissionApi(changed.apiBaseUrl).getMyHolds('000Student-1')
    assert.deepEqual(seen, [
      { path: '/first/me/holds', student: '000Student-1' },
      { path: '/changed-without-rebuild/me/holds', student: '000Student-1' },
    ])
  } finally {
    server.closeAllConnections()
    await new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve()))
  }
})
