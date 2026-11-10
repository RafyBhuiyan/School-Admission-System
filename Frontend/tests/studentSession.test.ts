import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createStudentSession, STUDENT_STORAGE_KEY, validStudentId } from '../src/studentSession.ts'
import { ApiError } from '../src/api/errors.ts'
import type { StudentHold } from '../src/api/types.ts'

const first = '00000000000000001'
const second = '00000000000000002'
const application: StudentHold = { holdId: 'one', classId: 'class', className: 'Morning', status: 'HELD', expiresAt: '2030-01-01T00:00:00Z' }
function storage(initial?: string) {
  const data = new Map<string, string>(initial === undefined ? [] : [[STUDENT_STORAGE_KEY, initial]])
  return { getItem: (key: string) => data.get(key) ?? null, setItem: (key: string, value: string) => { data.set(key, value) }, removeItem: (key: string) => { data.delete(key) } }
}
function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

test('valid student IDs preserve leading zeros and trim surrounding whitespace', () => {
  assert.equal(validStudentId(` ${first} `), first)
  assert.equal(validStudentId('00000000000000000'), '00000000000000000')
})
for (const value of ['', ' ', '1234567890123456', '123456789012345678', '1234567890123456x', '১২৩৪৫৬৭৮৯০১২৩৪৫৬৭', '12345678 901234567']) {
  test(`rejects invalid identification input ${JSON.stringify(value)}`, async () => {
    let calls = 0
    const saved = storage()
    const session = createStudentSession({ getMyHolds: async () => { calls++; return [] } }, saved)
    assert.equal(validStudentId(value), null)
    assert.equal(await session.identify(value), false)
    assert.equal(session.getSnapshot().studentId, null)
    assert.equal(saved.getItem(STUDENT_STORAGE_KEY), null)
    assert.equal(calls, 0)
  })
}

test('identification persists a validated ID and immediately fetches applications', async () => {
  const saved = storage()
  const session = createStudentSession({ getMyHolds: async (id, options) => {
    assert.equal(id, first)
    assert.equal(saved.getItem(STUDENT_STORAGE_KEY), first)
    assert.ok(options?.signal)
    return [application]
  } }, saved)
  assert.equal(await session.identify(first), true)
  assert.equal(session.getSnapshot().phase, 'ready')
  assert.deepEqual(session.getSnapshot().applications, [application])
})

test('a new session restores the saved ID and automatically loads history on startup', async () => {
  const saved = storage()
  await createStudentSession({ getMyHolds: async () => [] }, saved).identify(first)
  let calls = 0
  const reopened = createStudentSession({ getMyHolds: async id => { assert.equal(id, first); calls++; return [application] } }, saved)
  assert.equal(reopened.getSnapshot().studentId, first)
  await reopened.start()
  assert.equal(calls, 1)
  assert.deepEqual(reopened.getSnapshot().applications, [application])
  reopened.stop()
})

test('malformed saved IDs are never restored or sent to the API', async () => {
  for (const raw of ['student-1', ` ${first} `, '{"id":"bad"}', '123']) {
    const session = createStudentSession({ getMyHolds: async () => { throw new Error('Must not fetch') } }, storage(raw))
    await session.start()
    assert.equal(session.getSnapshot().studentId, null)
    assert.equal(session.getSnapshot().phase, 'idle')
  }
})

test('forget cancels requests, clears history/storage, and ignores a late successful response', async () => {
  const pending = deferred<StudentHold[]>()
  let signal: AbortSignal | undefined
  const saved = storage(first)
  const session = createStudentSession({ getMyHolds: async (_id, options) => { signal = options?.signal; return pending.promise } }, saved)
  const request = session.start()
  session.forget()
  assert.ok(signal?.aborted)
  assert.equal(saved.getItem(STUDENT_STORAGE_KEY), null)
  pending.resolve([application])
  await request
  assert.deepEqual(session.getSnapshot(), { studentId: null, applications: [], phase: 'idle', error: null, notice: null })
})

test('changing students replaces persistence and stale responses cannot replace the new history', async () => {
  const old = deferred<StudentHold[]>()
  const saved = storage(first)
  const session = createStudentSession({ getMyHolds: async id => id === first ? old.promise : [{ ...application, className: 'Day' }] }, saved)
  const initial = session.start()
  session.changeStudent()
  assert.equal(saved.getItem(STUDENT_STORAGE_KEY), null)
  assert.deepEqual(session.getSnapshot().applications, [])
  await session.identify(second)
  old.resolve([application])
  await initial
  assert.equal(session.getSnapshot().studentId, second)
  assert.equal(saved.getItem(STUDENT_STORAGE_KEY), second)
  assert.equal(session.getSnapshot().applications[0].className, 'Day')
  session.stop()
})

test('stale failures cannot expose an old request error after forgetting', async () => {
  const pending = deferred<StudentHold[]>()
  const session = createStudentSession({ getMyHolds: async () => pending.promise }, storage(first))
  const request = session.start()
  session.forget()
  pending.reject(new ApiError('network', 'Old failure'))
  await request
  assert.equal(session.getSnapshot().error, null)
  assert.equal(session.getSnapshot().studentId, null)
})

test('an unavailable API keeps the selected ID and supports retry', async () => {
  let calls = 0
  const saved = storage()
  const session = createStudentSession({ getMyHolds: async () => {
    if (++calls === 1) throw new ApiError('network', 'Server unavailable')
    return [application]
  } }, saved)
  await session.identify(first)
  assert.equal(session.getSnapshot().phase, 'error')
  assert.equal(session.getSnapshot().error, 'Server unavailable')
  assert.equal(saved.getItem(STUDENT_STORAGE_KEY), first)
  await session.refresh()
  assert.equal(session.getSnapshot().phase, 'ready')
  assert.equal(session.getSnapshot().error, null)
})

test('blocked storage permits a temporary session and explains save/clear failures', async () => {
  const blocked = { getItem: () => { throw new Error('blocked') }, setItem: () => { throw new Error('blocked') }, removeItem: () => { throw new Error('blocked') } }
  const session = createStudentSession({ getMyHolds: async () => [application] }, blocked)
  assert.ok(session.getSnapshot().notice)
  await session.identify(first)
  assert.equal(session.getSnapshot().phase, 'ready')
  assert.ok(session.getSnapshot().notice?.includes('could not remember'))
  session.forget()
  assert.equal(session.getSnapshot().studentId, null)
  assert.ok(session.getSnapshot().notice?.includes('could not be cleared'))
})

test('missing storage permits temporary access and a warning', async () => {
  const session = createStudentSession({ getMyHolds: async () => [] }, null)
  await session.identify(first)
  assert.equal(session.getSnapshot().studentId, first)
  assert.ok(session.getSnapshot().notice)
})

test('effect cleanup/restart aborts the old load and only the latest result wins', async () => {
  const old = deferred<StudentHold[]>()
  let calls = 0
  const session = createStudentSession({ getMyHolds: async () => ++calls === 1 ? old.promise : [] }, storage(first))
  const initial = session.start()
  session.stop()
  await session.start()
  old.resolve([application])
  await initial
  assert.deepEqual(session.getSnapshot().applications, [])
  assert.equal(session.getSnapshot().phase, 'ready')
})

test('storage changes from another tab replace or clear the active history', async () => {
  const saved = storage(first)
  const session = createStudentSession({ getMyHolds: async () => [application] }, saved)
  await session.start()
  saved.setItem(STUDENT_STORAGE_KEY, second)
  await session.restore()
  assert.equal(session.getSnapshot().studentId, second)
  saved.removeItem(STUDENT_STORAGE_KEY)
  await session.restore()
  assert.equal(session.getSnapshot().studentId, null)
  assert.deepEqual(session.getSnapshot().applications, [])
})
