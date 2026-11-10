import assert from 'node:assert/strict'
import { test } from 'node:test'
import { createClassCatalog, validateClassInput } from '../src/classCatalog.ts'
import { createStudentSession } from '../src/studentSession.ts'
import { ApiError } from '../src/api/errors.ts'
import type { AdmissionApi } from '../src/api/client.ts'
import type { ClassSummary, StudentHold } from '../src/api/types.ts'

const id = '11111111-1111-1111-1111-111111111111'
const studentId = '00000000000000001'
const row: ClassSummary = { id, name: 'Morning', totalSeats: 10, heldSeats: 2, confirmedSeats: 3, availableSeats: 5 }
const hold = { holdId: '22222222-2222-2222-2222-222222222222', classId: id, studentId, status: 'HELD' as const, expiresAt: '2030-01-01T00:00:00Z' }
const history: StudentHold = { ...hold, className: row.name }
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(yes => { resolve = yes })
  return { promise, resolve }
}
function setup(overrides: Partial<Pick<AdmissionApi, 'getClasses' | 'createClass' | 'createHold' | 'getMyHolds'>> = {}) {
  let scheduled: (() => void) | null = null
  let delay = 0
  const api = {
    getClasses: async () => [row], createClass: async () => ({ id, name: 'New class', totalSeats: 5 }),
    createHold: async () => hold, getMyHolds: async () => [] as StudentHold[], ...overrides,
  }
  const data = new Map<string, string>()
  const student = createStudentSession(api, { getItem: key => data.get(key) ?? null, setItem: (key, value) => { data.set(key, value) }, removeItem: key => { data.delete(key) } })
  const catalog = createClassCatalog(api, student, {
    set: (callback, milliseconds) => { scheduled = callback; delay = milliseconds; return callback },
    clear: () => { scheduled = null },
  })
  return { api, student, catalog, timer: () => scheduled, delay: () => delay }
}

test('class input trims names and accepts positive integer boundaries', () => {
  assert.deepEqual(validateClassInput('  Morning  ', ' 10 ').value, { name: 'Morning', totalSeats: 10 })
  assert.equal(validateClassInput('a'.repeat(200), '2147483647').value?.totalSeats, 2147483647)
})
for (const [name, seats, field] of [ ['', '10', 'name'], ['  ', '10', 'name'], ['a'.repeat(201), '10', 'name'],
  ['Class', '', 'seats'], ['Class', '0', 'seats'], ['Class', '-1', 'seats'], ['Class', '1.5', 'seats'],
  ['Class', '1e3', 'seats'], ['Class', '2147483648', 'seats'] ]) {
  test(`invalid class input rejects ${field}: ${name.length > 20 ? 'overlong name' : name}/${seats}`, () => {
    const input = validateClassInput(name, seats)
    assert.equal(input.value, undefined)
    assert.ok(input.errors[field])
  })
}

test('startup loads all seat fields and schedules a four-second refresh', async () => {
  const { catalog, timer, delay } = setup()
  await catalog.start()
  assert.deepEqual(catalog.getSnapshot().classes, [row])
  assert.equal(catalog.getSnapshot().loading, false)
  assert.equal(delay(), 4000)
  assert.ok(timer())
  catalog.stop()
  assert.equal(timer(), null)
})

test('polling pauses while hidden and resumes with a fresh request', async () => {
  let reads = 0
  const { catalog, timer } = setup({ getClasses: async () => { reads++; return [row] } })
  await catalog.start()
  catalog.setVisible(false)
  assert.equal(timer(), null)
  catalog.setVisible(true)
  await Promise.resolve()
  assert.equal(reads, 2)
  catalog.stop()
})

test('a stopped or superseded read cannot replace newer classes', async () => {
  const old = deferred<ClassSummary[]>()
  let reads = 0
  const { catalog } = setup({ getClasses: async () => ++reads === 1 ? old.promise : [] })
  const initial = catalog.start()
  await catalog.refresh()
  old.resolve([row])
  await initial
  assert.deepEqual(catalog.getSnapshot().classes, [])
  catalog.stop()
})

test('invalid creation never reaches the API and valid creation refreshes classes', async () => {
  let writes = 0, reads = 0
  const { catalog } = setup({ getClasses: async () => { reads++; return [] }, createClass: async body => {
    writes++; assert.deepEqual(body, { name: 'New', totalSeats: 5 }); return { id, name: 'New', totalSeats: 5 }
  } })
  await catalog.start()
  assert.equal(await catalog.create('', '0'), false)
  assert.equal(writes, 0)
  assert.equal(await catalog.create(' New ', '5'), true)
  assert.equal(writes, 1)
  assert.equal(reads, 2)
  assert.equal(catalog.getSnapshot().message, 'Class created.')
  catalog.stop()
})

test('duplicate creates are blocked while the first write is pending', async () => {
  const pending = deferred<{ id: string; name: string; totalSeats: number }>()
  let writes = 0
  const { catalog } = setup({ createClass: async () => { writes++; return pending.promise } })
  await catalog.start()
  const first = catalog.create('New', '5')
  assert.equal(await catalog.create('New', '5'), false)
  pending.resolve({ id, name: 'New', totalSeats: 5 })
  await first
  assert.equal(writes, 1)
  catalog.stop()
})

test('server validation and duplicate-name conflicts remain visible', async () => {
  let attempt = 0, reads = 0
  const { catalog } = setup({ getClasses: async () => { reads++; return [] }, createClass: async () => {
    if (++attempt === 1) throw new ApiError('http', 'Invalid class.', { status: 400, errors: { Name: ['Choose a name.'] } })
    throw new ApiError('http', 'Class already exists.', { status: 409 })
  } })
  await catalog.start()
  assert.equal(await catalog.create('Name', '1'), false)
  assert.equal(catalog.getSnapshot().fieldErrors.name, 'Choose a name.')
  assert.equal(await catalog.create('Name', '1'), false)
  assert.equal(catalog.getSnapshot().mutationError, 'Class already exists.')
  assert.equal(catalog.getSnapshot().creating, false)
  assert.equal(reads, 2)
  catalog.stop()
})

test('apply requires a student and rejects full/already-applied classes without writing', async () => {
  let writes = 0
  const { catalog, student } = setup({ createHold: async () => { writes++; return hold }, getMyHolds: async () => [history] })
  await catalog.start()
  assert.equal(await catalog.apply(id), false)
  await student.identify(studentId)
  assert.equal(await catalog.apply(id), false)
  assert.equal(writes, 0)
  catalog.stop()
  const full = setup({ getClasses: async () => [{ ...row, availableSeats: 0 }] })
  await full.student.identify(studentId)
  await full.catalog.start()
  assert.equal(await full.catalog.apply(id), false)
  full.catalog.stop()
})

test('successful apply attaches the current identity and refreshes counts and applications', async () => {
  let created = false, reads = 0
  const { catalog, student } = setup({
    getClasses: async () => { reads++; return [created ? { ...row, heldSeats: 3, availableSeats: 4 } : row] },
    getMyHolds: async () => created ? [history] : [],
    createHold: async (classId, selected, options) => {
      assert.equal(classId, id); assert.equal(selected, studentId); assert.ok(options?.signal)
      created = true; return hold
    },
  })
  await student.identify(studentId)
  await catalog.start()
  assert.equal(await catalog.apply(id), true)
  assert.equal(reads, 2)
  assert.equal(catalog.getSnapshot().classes[0].availableSeats, 4)
  assert.deepEqual(student.getSnapshot().applications, [history])
  assert.ok(catalog.getSnapshot().message?.includes('temporarily held'))
  assert.equal(await catalog.apply(id), false)
  catalog.stop()
})

test('a pending apply is single-flight and student changes abort and suppress its response', async () => {
  const pending = deferred<typeof hold>()
  let signal: AbortSignal | undefined, writes = 0
  const { catalog, student } = setup({ createHold: async (_class, _student, options) => { writes++; signal = options?.signal; return pending.promise } })
  await student.identify(studentId)
  await catalog.start()
  const first = catalog.apply(id)
  assert.equal(await catalog.apply(id), false)
  student.forget()
  assert.ok(signal?.aborted)
  pending.resolve(hold)
  assert.equal(await first, false)
  assert.equal(writes, 1)
  assert.equal(catalog.getSnapshot().message, null)
  assert.equal(catalog.getSnapshot().applyingId, null)
  assert.deepEqual(student.getSnapshot().applications, [])
  catalog.stop()
})

test('apply conflicts refresh authoritative state and network failures never retry mutations', async () => {
  let writes = 0, reads = 0
  const { catalog, student } = setup({ getClasses: async () => { reads++; return [row] }, createHold: async () => {
    writes++
    throw writes === 1 ? new ApiError('http', 'No seat remains.', { status: 409 }) : new ApiError('network', 'Connection lost.')
  } })
  await student.identify(studentId)
  await catalog.start()
  assert.equal(await catalog.apply(id), false)
  assert.equal(reads, 2)
  assert.equal(catalog.getSnapshot().mutationError, 'No seat remains.')
  assert.equal(await catalog.apply(id), false)
  assert.equal(writes, 2)
  assert.equal(reads, 2)
  assert.equal(catalog.getSnapshot().applyingId, null)
  catalog.stop()
})

test('read failures preserve the last snapshot and can be retried', async () => {
  let reads = 0
  const { catalog } = setup({ getClasses: async () => {
    if (++reads === 2) throw new ApiError('network', 'Offline')
    return [row]
  } })
  await catalog.start()
  await catalog.refresh()
  assert.equal(catalog.getSnapshot().error, 'Offline')
  assert.deepEqual(catalog.getSnapshot().classes, [row])
  await catalog.refresh()
  assert.equal(catalog.getSnapshot().error, null)
  catalog.stop()
})
