import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createStudentSession, STUDENT_STORAGE_KEY } from '../src/studentSession.ts'
import { ApiError } from '../src/api/errors.ts'
import type { StudentHold } from '../src/api/types.ts'

const first = '00000000000000001', second = '00000000000000002'
const held: StudentHold = { holdId: 'one', classId: 'morning', className: 'Morning', status: 'HELD', expiresAt: '2030-01-01T00:00:00Z' }
function storage() {
  const data = new Map([[STUDENT_STORAGE_KEY, first]])
  return { getItem: (key: string) => data.get(key) ?? null, setItem: (key: string, value: string) => { data.set(key, value) }, removeItem: (key: string) => { data.delete(key) } }
}
function deferred<T>() {
  let resolve!: (value: T) => void, reject!: (reason: unknown) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}
async function flush() { for (let index = 0; index < 12; index++) await Promise.resolve() }
function scheduler() {
  let id = 0
  const tasks = new Map<number, { callback: () => void; delay: number }>()
  return {
    tasks,
    set: (callback: () => void, delay: number) => { tasks.set(++id, { callback, delay }); return id },
    clear: (handle: unknown) => { tasks.delete(handle as number) },
    async fire() {
      assert.equal(tasks.size, 1)
      const [key, task] = [...tasks.entries()][0]
      tasks.delete(key); task.callback(); await flush()
    },
  }
}

test('reopened history restores identity and fetches every status and class from the API', async () => {
  const saved = storage(), time = scheduler()
  const history: StudentHold[] = ['HELD', 'CONFIRMED', 'EXPIRED', 'CANCELLED'].map((status, index) => ({
    ...held, holdId: String(index), classId: `class-${index}`, className: `Class ${index}`, status: status as StudentHold['status'],
  }))
  let calls = 0
  const session = createStudentSession({ getMyHolds: async id => { assert.equal(id, first); calls++; return history } }, saved, time)
  await session.start()
  assert.equal(calls, 1)
  assert.deepEqual(session.getSnapshot().applications, history)
  session.stop()
  // Only identity is remembered; reopening must obtain changed status from the server.
  const reopened = createStudentSession({ getMyHolds: async () => history.map(row => ({ ...row, status: 'EXPIRED' })) }, saved, time)
  await reopened.start()
  assert.ok(reopened.getSnapshot().applications.every(row => row.status === 'EXPIRED'))
  assert.equal(time.tasks.size, 0)
  reopened.stop()
})

test('active history polls after 2.5 seconds without clearing rows, flashing loading, or overlapping reads', async () => {
  const pending = deferred<StudentHold[]>(), time = scheduler()
  let reads = 0
  const session = createStudentSession({ getMyHolds: async () => ++reads === 1 ? [held] : pending.promise }, storage(), time)
  await session.start()
  assert.equal([...time.tasks.values()][0].delay, 2500)
  await time.fire()
  assert.equal(session.getSnapshot().phase, 'ready')
  assert.deepEqual(session.getSnapshot().applications, [held])
  assert.equal(time.tasks.size, 0)
  assert.equal(reads, 2)
  pending.resolve([{ ...held, status: 'EXPIRED' }]); await flush()
  assert.equal(session.getSnapshot().applications[0].status, 'EXPIRED')
  assert.equal(time.tasks.size, 0)
  session.stop()
})

for (const status of ['CONFIRMED', 'EXPIRED', 'CANCELLED', 'empty'] as const) {
  test(`${status} history makes no periodic requests`, async () => {
    const time = scheduler()
    const session = createStudentSession({ getMyHolds: async () => status === 'empty' ? [] : [{ ...held, status }] }, storage(), time)
    await session.start()
    assert.equal(session.getSnapshot().phase, 'ready')
    assert.equal(time.tasks.size, 0)
    session.stop()
  })
}

test('hidden pages pause history polling and becoming visible fetches current server status', async () => {
  const time = scheduler()
  let reads = 0
  const session = createStudentSession({ getMyHolds: async () => [{ ...held, status: ++reads === 1 ? 'HELD' : 'CONFIRMED' }] }, storage(), time)
  await session.start()
  session.setVisible(false)
  assert.equal(time.tasks.size, 0)
  session.setVisible(true); await flush()
  assert.equal(reads, 2)
  assert.equal(session.getSnapshot().applications[0].status, 'CONFIRMED')
  assert.equal(time.tasks.size, 0)
  session.stop()
})

test('forget during a periodic read removes persistence and ignores a late response', async () => {
  const pending = deferred<StudentHold[]>(), time = scheduler(), saved = storage()
  let reads = 0, signal: AbortSignal | undefined
  const session = createStudentSession({ getMyHolds: async (_id, options) => {
    signal = options?.signal; return ++reads === 1 ? [held] : pending.promise
  } }, saved, time)
  await session.start(); await time.fire()
  session.forget()
  assert.ok(signal?.aborted)
  assert.equal(saved.getItem(STUDENT_STORAGE_KEY), null)
  pending.resolve([held]); await flush()
  assert.equal(session.getSnapshot().studentId, null)
  assert.deepEqual(session.getSnapshot().applications, [])
  assert.equal(time.tasks.size, 0)
  const reopened = createStudentSession({ getMyHolds: async () => { throw new Error('Must not fetch') } }, saved, time)
  await reopened.start()
  assert.equal(reopened.getSnapshot().studentId, null)
  session.stop(); reopened.stop()
})

test('changing student during polling cannot restore the previous student or history', async () => {
  const pending = deferred<StudentHold[]>(), time = scheduler(), saved = storage()
  let firstReads = 0
  const other: StudentHold = { ...held, className: 'Afternoon', status: 'CANCELLED' }
  const session = createStudentSession({ getMyHolds: async id => id === second ? [other] : ++firstReads === 1 ? [held] : pending.promise }, saved, time)
  await session.start(); await time.fire()
  session.changeStudent()
  await session.identify(second)
  pending.resolve([held]); await flush()
  assert.equal(saved.getItem(STUDENT_STORAGE_KEY), second)
  assert.deepEqual(session.getSnapshot().applications, [other])
  assert.equal(time.tasks.size, 0)
  session.stop()
})

test('a background error retains history, reports failure, and retries while a hold remains', async () => {
  const time = scheduler()
  let reads = 0
  const session = createStudentSession({ getMyHolds: async () => {
    if (++reads === 2) throw new ApiError('network', 'Unavailable')
    return [{ ...held, status: reads > 2 ? 'CANCELLED' : 'HELD' }]
  } }, storage(), time)
  await session.start(); await time.fire()
  assert.equal(session.getSnapshot().error, 'Unavailable')
  assert.deepEqual(session.getSnapshot().applications, [held])
  await time.fire()
  assert.equal(session.getSnapshot().error, null)
  assert.equal(session.getSnapshot().applications[0].status, 'CANCELLED')
  assert.equal(time.tasks.size, 0)
  session.stop()
})

test('manual refresh supersedes an old periodic read and retains exactly one next poll', async () => {
  const pending = deferred<StudentHold[]>(), time = scheduler()
  let reads = 0, oldSignal: AbortSignal | undefined
  const session = createStudentSession({ getMyHolds: async (_id, options) => {
    if (++reads === 2) { oldSignal = options?.signal; return pending.promise }
    return [held]
  } }, storage(), time)
  await session.start(); await time.fire()
  await session.refresh()
  assert.ok(oldSignal?.aborted)
  pending.resolve([]); await flush()
  assert.deepEqual(session.getSnapshot().applications, [held])
  assert.equal(time.tasks.size, 1)
  session.stop()
  assert.equal(time.tasks.size, 0)
})

test('cleanup aborts a periodic read and a restarted view loads fresh history', async () => {
  const pending = deferred<StudentHold[]>(), time = scheduler()
  let reads = 0
  const session = createStudentSession({ getMyHolds: async () => ++reads === 2 ? pending.promise : reads === 1 ? [held] : [] }, storage(), time)
  await session.start(); await time.fire()
  session.stop()
  await session.start()
  pending.resolve([held]); await flush()
  assert.deepEqual(session.getSnapshot().applications, [])
  assert.equal(time.tasks.size, 0)
  session.stop()
})

test('a newly created hold starts polling after the ordinary history refresh', async () => {
  const time = scheduler()
  let rows: StudentHold[] = []
  const session = createStudentSession({ getMyHolds: async () => rows }, storage(), time)
  await session.start()
  assert.equal(time.tasks.size, 0)
  rows = [held]
  await session.refresh()
  assert.equal(time.tasks.size, 1)
  session.stop()
})
