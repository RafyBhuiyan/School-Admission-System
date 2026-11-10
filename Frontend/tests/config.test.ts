import assert from 'node:assert/strict'
import { test } from 'node:test'
import { loadConfig, parseConfig } from '../src/config.ts'

test('configuration normalizes the API origin and retains a path prefix', () => {
  assert.deepEqual(parseConfig({ API_BASE_URL: ' http://localhost:8080/ ' }), { apiBaseUrl: 'http://localhost:8080' })
  assert.deepEqual(parseConfig({ API_BASE_URL: 'https://example.org/admission/api/' }), { apiBaseUrl: 'https://example.org/admission/api' })
})

for (const value of [null, {}, { API_BASE_URL: 123 }, { API_BASE_URL: '' }, { API_BASE_URL: '/api' },
  { API_BASE_URL: 'file:///tmp/api' }, { API_BASE_URL: 'javascript:alert(1)' }, { API_BASE_URL: 'https://user:password@example.org' },
  { API_BASE_URL: 'https://example.org?student=x' }, { API_BASE_URL: 'https://example.org#fragment' }]) {
  test(`rejects invalid configuration ${JSON.stringify(value)}`, () => assert.throws(() => parseConfig(value)))
}

test('loads runtime JSON with cache disabled and no credentials or redirects', async () => {
  const fetcher: typeof fetch = async (path, options) => {
    assert.equal(path, '/admission/config.json')
    assert.equal(options?.cache, 'no-store')
    assert.equal(options?.credentials, 'omit')
    assert.equal(options?.redirect, 'error')
    assert.ok(options?.signal)
    return Response.json({ API_BASE_URL: 'http://localhost:8080' })
  }
  assert.deepEqual(await loadConfig('/admission/config.json', fetcher), { apiBaseUrl: 'http://localhost:8080' })
})

test('does not silently default when runtime configuration is missing or malformed', async () => {
  await assert.rejects(loadConfig('/config.json', async () => new Response('missing', { status: 404 })))
  await assert.rejects(loadConfig('/config.json', async () => new Response('<html>not json</html>')))
  await assert.rejects(loadConfig('/config.json', async () => Response.json({})))
})
