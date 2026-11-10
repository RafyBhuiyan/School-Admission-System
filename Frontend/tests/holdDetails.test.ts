import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createHoldDetails, formatCountdown } from '../src/holdDetails.ts'
import { createStudentSession } from '../src/studentSession.ts'
import { ApiError } from '../src/api/errors.ts'
import type { AdmissionApi } from '../src/api/client.ts'
import type { ConfirmedHold, HoldDetails, HoldStatus, StudentHold } from '../src/api/types.ts'

const holdId = '11111111-1111-1111-1111-111111111111'
const classId = '22222222-2222-2222-2222-222222222222'
const studentId = '00000000000000001'
const epoch = Date.parse('2030-01-01T00:00:00Z')
const selection: StudentHold = { holdId, classId, className: 'Morning', status: 'HELD', expiresAt: new Date(epoch + 10000).toISOString() }
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(yes => { resolve = yes })
  return { promise, resolve }
}
async function flush() { for (let index = 0; index < 12; index++) await Promise.resolve() }
function clock() {
  let now = 0, id = 0
  const tasks = new Map<number, { callback: () => void; at: number; delay: number }>()
  return {
    now: () => now,
    set: (callback: () => void, delay: number) => { tasks.set(++id, { callback, delay, at: now + delay }); return id },
    clear: (handle: unknown) => { tasks.delete(handle as number) },
    tasks,
    async advance(ms: number) {
      const target = now + ms
      while (true) {
        const next = [...tasks.entries()].filter(([, task]) => task.at <= target).sort((a, b) => a[1].at - b[1].at)[0]
        if (!next) break
        now = next[1].at; tasks.delete(next[0]); next[1].callback(); await flush()
      }
      now = target; await flush()
    },
  }
}
type Methods = Pick<AdmissionApi, 'getHold' | 'confirmHold' | 'cancelHold'>
async function setup(overrides: Partial<Methods> = {}) {
  const time = clock()
  let status: HoldStatus = 'HELD', reads = 0, confirms = 0, cancels = 0, changes = 0
  const fixture = (): HoldDetails => ({ holdId, classId, status, expiresAt: selection.expiresAt, serverTime: new Date(epoch + time.now()).toISOString() })
  const student = createStudentSession({ getMyHolds: async () => [selection] }, null)
  await student.identify(studentId)
  const api: Methods = {
    getHold: async () => { reads++; return fixture() },
    confirmHold: async (id, student, options) => {
      assert.equal(id, holdId); assert.equal(student, studentId); assert.ok(options?.signal)
      confirms++; status = 'CONFIRMED'; return { holdId, status: 'CONFIRMED' }
    },
    cancelHold: async (id, student, options) => {
      assert.equal(id, holdId); assert.equal(student, studentId); assert.ok(options?.signal)
      cancels++; status = 'CANCELLED'
    },
    ...overrides,
  }
  const controller = createHoldDetails(api, student, async () => { changes++ }, time)
  controller.start()
  return { controller, student, api, time, fixture, setStatus: (next: HoldStatus) => { status = next },
    counts: () => ({ reads, confirms, cancels, changes }) }
}

test('countdown formatting clamps zero and keeps minute/second units', () => {
  assert.equal(formatCountdown(-1), '00:00')
  assert.equal(formatCountdown(9), '00:09')
  assert.equal(formatCountdown(600), '10:00')
})

test('server time anchors the countdown independently of the computer wall clock', async () => {
  const { controller, time } = await setup()
  await controller.open(selection)
  assert.equal(controller.getSnapshot().remaining, 10) // Server epoch is deliberately far from Date.now().
  assert.equal(controller.canAct(), true)
  assert.ok([...time.tasks.values()].some(task => task.delay === 2500))
  await time.advance(1000)
  assert.equal(controller.getSnapshot().remaining, 9)
  controller.stop()
})

test('response transit time never extends the countdown', async () => {
  const pending = deferred<HoldDetails>()
  const { controller, time, fixture } = await setup({ getHold: async () => pending.promise })
  const response = fixture()
  const loading = controller.open(selection)
  await time.advance(4000)
  pending.resolve(response)
  await loading
  assert.equal(controller.getSnapshot().remaining, 6)
  controller.stop()
})

for (const status of ['CONFIRMED', 'EXPIRED', 'CANCELLED'] as const) {
  test(`${status} has no action or countdown timers`, async () => {
    const { controller, time, setStatus, counts } = await setup()
    setStatus(status)
    await controller.open(selection)
    assert.equal(controller.getSnapshot().details?.status, status)
    assert.equal(controller.getSnapshot().remaining, null)
    assert.equal(controller.canAct(), false)
    assert.equal(await controller.confirm(), false)
    assert.equal(await controller.cancel(), false)
    assert.equal(time.tasks.size, 0)
    assert.equal(counts().confirms + counts().cancels, 0)
    controller.stop()
  })
}

test('estimated zero disables actions while the backend decides the final status', async () => {
  const pending = deferred<HoldDetails>()
  const state = await setup()
  state.api.getHold = async () => state.time.now() >= 10000 ? pending.promise : state.fixture()
  await state.controller.open(selection)
  await state.time.advance(10000)
  assert.equal(state.controller.getSnapshot().remaining, 0)
  assert.equal(state.controller.getSnapshot().checkingExpiry, true)
  assert.equal(state.controller.getSnapshot().details?.status, 'HELD')
  assert.equal(state.controller.canAct(), false)
  assert.equal(await state.controller.confirm(), false)
  assert.equal(state.counts().confirms, 0)
  pending.resolve({ ...state.fixture(), status: 'EXPIRED' })
  await flush()
  assert.equal(state.controller.getSnapshot().details?.status, 'EXPIRED')
  assert.equal(state.controller.getSnapshot().checkingExpiry, false)
  assert.ok(state.counts().changes >= 1)
  state.controller.stop()
})

test('a HELD response with zero server duration never enables actions or loops immediately', async () => {
  const state = await setup()
  state.api.getHold = async () => ({ ...state.fixture(), serverTime: selection.expiresAt })
  await state.controller.open(selection)
  assert.equal(state.controller.canAct(), false)
  assert.equal(state.controller.getSnapshot().remaining, 0)
  assert.equal([...state.time.tasks.values()].filter(task => task.delay === 2500).length, 1)
  state.controller.stop()
})

test('polling a terminal transition refreshes related history and availability', async () => {
  const state = await setup()
  await state.controller.open(selection)
  state.setStatus('CONFIRMED')
  await state.time.advance(2500)
  assert.equal(state.controller.getSnapshot().details?.status, 'CONFIRMED')
  assert.equal(state.counts().changes, 1)
  assert.equal(state.time.tasks.size, 0)
  state.controller.stop()
})

for (const action of ['confirm', 'cancel'] as const) {
  test(`successful ${action} refreshes details and related data and blocks terminal retries`, async () => {
    const state = await setup()
    await state.controller.open(selection)
    assert.equal(await state.controller[action](), true)
    assert.equal(state.controller.getSnapshot().details?.status, action === 'confirm' ? 'CONFIRMED' : 'CANCELLED')
    assert.equal(state.counts().changes, 1)
    assert.equal(state.counts().reads, 2)
    assert.equal(state.controller.canAct(), false)
    assert.equal(await state.controller[action](), false)
    assert.equal(state.counts().confirms + state.counts().cancels, 1)
    state.controller.stop()
  })
}

test('duplicate and competing mutations are blocked while confirmation is pending', async () => {
  const pending = deferred<ConfirmedHold>()
  let writes = 0
  const state = await setup({ confirmHold: async () => { writes++; return pending.promise } })
  await state.controller.open(selection)
  const first = state.controller.confirm()
  assert.equal(await state.controller.confirm(), false)
  assert.equal(await state.controller.cancel(), false)
  assert.equal(writes, 1)
  state.setStatus('CONFIRMED')
  pending.resolve({ holdId, status: 'CONFIRMED' })
  await first
  state.controller.stop()
})

test('409 reconciles an expired hold without retrying confirmation', async () => {
  const state = await setup()
  let writes = 0
  state.api.confirmHold = async () => { writes++; state.setStatus('EXPIRED'); throw new ApiError('http', 'Hold expired.', { status: 409 }) }
  await state.controller.open(selection)
  assert.equal(await state.controller.confirm(), false)
  assert.equal(state.controller.getSnapshot().details?.status, 'EXPIRED')
  assert.equal(state.controller.getSnapshot().actionError, 'Hold expired.')
  assert.equal(writes, 1)
  assert.equal(state.counts().changes, 1)
  state.controller.stop()
})

test('403 disables further mutations even if the public detail endpoint returns HELD', async () => {
  const state = await setup({ cancelHold: async () => { throw new ApiError('http', 'Forbidden.', { status: 403 }) } })
  await state.controller.open(selection)
  assert.equal(await state.controller.cancel(), false)
  assert.equal(state.controller.getSnapshot().details?.status, 'HELD')
  assert.equal(state.controller.canAct(), false)
  assert.equal(state.controller.getSnapshot().blocked, true)
  state.controller.stop()
})

test('a lost mutation response is reconciled to the committed server state without retry', async () => {
  const state = await setup()
  let writes = 0
  state.api.confirmHold = async () => { writes++; state.setStatus('CONFIRMED'); throw new ApiError('network', 'Response lost.') }
  await state.controller.open(selection)
  await state.controller.confirm()
  assert.equal(state.controller.getSnapshot().details?.status, 'CONFIRMED')
  assert.equal(state.controller.canAct(), false)
  assert.equal(writes, 1)
  state.controller.stop()
})

test('a successful mutation remains terminal when its verification read fails', async () => {
  const state = await setup()
  await state.controller.open(selection)
  state.api.getHold = async () => { throw new ApiError('network', 'Offline') }
  assert.equal(await state.controller.confirm(), true)
  assert.equal(state.controller.getSnapshot().details?.status, 'CONFIRMED')
  assert.equal(state.controller.getSnapshot().error, 'Offline')
  assert.equal(state.controller.canAct(), false)
  state.controller.stop()
})

test('read errors disable mutations and allow a manual retry', async () => {
  const state = await setup()
  state.api.getHold = async () => { throw new ApiError('network', 'Offline') }
  await state.controller.open(selection)
  assert.equal(state.controller.getSnapshot().error, 'Offline')
  assert.equal(state.controller.canAct(), false)
  state.api.getHold = async () => state.fixture()
  await state.controller.refresh()
  assert.equal(state.controller.getSnapshot().error, null)
  assert.equal(state.controller.canAct(), true)
  state.controller.stop()
})

test('missing holds show an error without endless automatic polling', async () => {
  const state = await setup({ getHold: async () => { throw new ApiError('http', 'Not found', { status: 404 }) } })
  await state.controller.open(selection)
  assert.equal(state.controller.getSnapshot().details, null)
  assert.equal(state.controller.getSnapshot().error, 'Not found')
  assert.equal(state.time.tasks.size, 0)
  state.controller.stop()
})

test('malformed server timing fails closed', async () => {
  const state = await setup()
  state.api.getHold = async () => ({ ...state.fixture(), serverTime: 'invalid' })
  await state.controller.open(selection)
  assert.ok(state.controller.getSnapshot().error)
  assert.equal(state.controller.canAct(), false)
  assert.equal(await state.controller.confirm(), false)
  state.controller.stop()
})

test('forgetting aborts a detail read and prevents a late response from restoring it', async () => {
  const pending = deferred<HoldDetails>()
  let signal: AbortSignal | undefined
  const state = await setup({ getHold: async (_id, options) => { signal = options?.signal; return pending.promise } })
  const first = state.controller.open(selection)
  state.student.forget()
  assert.ok(signal?.aborted)
  pending.resolve(state.fixture())
  await first
  assert.equal(state.controller.getSnapshot().selection, null)
  assert.equal(state.controller.getSnapshot().details, null)
  state.controller.stop()
})

test('changing students aborts a mutation and suppresses its success and refresh callbacks', async () => {
  const pending = deferred<ConfirmedHold>()
  let signal: AbortSignal | undefined
  const state = await setup({ confirmHold: async (_id, _student, options) => { signal = options?.signal; return pending.promise } })
  await state.controller.open(selection)
  const first = state.controller.confirm()
  await state.student.identify('00000000000000002')
  assert.ok(signal?.aborted)
  pending.resolve({ holdId, status: 'CONFIRMED' })
  assert.equal(await first, false)
  assert.equal(state.controller.getSnapshot().message, null)
  assert.equal(state.controller.getSnapshot().selection, null)
  assert.equal(state.counts().changes, 0)
  state.controller.stop()
})

test('visibility changes pause timers and force fresh status before enabling actions', async () => {
  const state = await setup()
  await state.controller.open(selection)
  state.controller.setVisible(false)
  assert.equal(state.time.tasks.size, 0)
  assert.equal(state.controller.canAct(), false)
  await state.time.advance(20000)
  state.setStatus('EXPIRED')
  state.controller.setVisible(true)
  assert.equal(state.controller.canAct(), false)
  await flush()
  assert.equal(state.controller.getSnapshot().details?.status, 'EXPIRED')
  assert.equal(state.counts().reads, 2)
  state.controller.stop()
})

test('opening another application ignores the earlier response and close clears timers', async () => {
  const pending = deferred<HoldDetails>()
  const other = { ...selection, holdId: '33333333-3333-3333-3333-333333333333', className: 'Day' }
  const state = await setup()
  state.api.getHold = async id => id === holdId ? pending.promise : { ...state.fixture(), holdId: other.holdId }
  const first = state.controller.open(selection)
  await state.controller.open(other)
  pending.resolve(state.fixture())
  await first
  assert.equal(state.controller.getSnapshot().details?.holdId, other.holdId)
  state.controller.close()
  assert.equal(state.controller.getSnapshot().selection, null)
  assert.equal(state.time.tasks.size, 0)
  state.controller.stop()
})
