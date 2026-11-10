import { ApiError } from './api/errors.ts'
import type { AdmissionApi } from './api/client.ts'
import type { ClassSummary, CreateClassRequest } from './api/types.ts'
import type { createStudentSession } from './studentSession.ts'

export function validateClassInput(name: string, seats: string): { value?: CreateClassRequest; errors: Record<string, string> } {
  const errors: Record<string, string> = {}
  const trimmed = name.trim()
  if (!trimmed || trimmed.length > 200) errors.name = 'Enter a class name of 1 to 200 characters.'
  const count = Number(seats.trim())
  if (!/^[0-9]+$/.test(seats.trim()) || !Number.isInteger(count) || count < 1 || count > 2147483647) {
    errors.seats = 'Enter a whole number of seats between 1 and 2147483647.'
  }
  return Object.keys(errors).length ? { errors } : { value: { name: trimmed, totalSeats: count }, errors }
}

export interface CatalogState {
  classes: ClassSummary[]; loading: boolean; error: string | null
  creating: boolean; applyingId: string | null; message: string | null; mutationError: string | null
  fieldErrors: Record<string, string>
}
type StudentSession = ReturnType<typeof createStudentSession>
type CatalogApi = Pick<AdmissionApi, 'getClasses' | 'createClass' | 'createHold'>
interface Scheduler { set: (callback: () => void, delay: number) => unknown; clear: (handle: unknown) => void }

export function createClassCatalog(api: CatalogApi, student: StudentSession, scheduler: Scheduler = {
  set: (callback, delay) => setTimeout(callback, delay), clear: handle => clearTimeout(handle as ReturnType<typeof setTimeout>),
}) {
  let state: CatalogState = { classes: [], loading: false, error: null, creating: false, applyingId: null, message: null, mutationError: null, fieldErrors: {} }
  const listeners = new Set<() => void>()
  let attached = false
  let visible = true
  let timer: unknown
  let readVersion = 0
  let lifecycle = 0
  let applyVersion = 0
  let read: AbortController | null = null
  let creation: AbortController | null = null
  let application: AbortController | null = null
  let unsubscribe: (() => void) | null = null
  let selected = student.getSnapshot().studentId
  function update(patch: Partial<CatalogState>) { state = { ...state, ...patch }; for (const listener of listeners) listener() }
  function clearTimer() { if (timer !== undefined) scheduler.clear(timer); timer = undefined }
  function message(error: unknown) { return error instanceof ApiError ? error.message : 'The request could not be completed. Please try again.' }

  async function refresh() {
    clearTimer()
    read?.abort()
    const version = ++readVersion
    const controller = new AbortController()
    read = controller
    update({ loading: true })
    try {
      const classes = await api.getClasses({ signal: controller.signal })
      if (attached && version === readVersion && !controller.signal.aborted) update({ classes, loading: false, error: null })
    } catch (error) {
      if (attached && version === readVersion && !controller.signal.aborted) update({ loading: false, error: message(error) })
    } finally {
      if (attached && visible && version === readVersion) timer = scheduler.set(() => { void refresh() }, 4000)
    }
  }

  async function start() {
    attached = true
    update({ creating: false, applyingId: null })
    selected = student.getSnapshot().studentId
    unsubscribe?.()
    unsubscribe = student.subscribe(() => {
      const id = student.getSnapshot().studentId
      if (id !== selected) {
        selected = id
        applyVersion++
        application?.abort()
        update({ applyingId: null, message: null, mutationError: null })
      }
    })
    await refresh()
  }
  function stop() {
    attached = false; lifecycle++; readVersion++; applyVersion++
    clearTimer(); read?.abort(); creation?.abort(); application?.abort(); unsubscribe?.(); unsubscribe = null
  }
  function setVisible(value: boolean) {
    if (visible === value) return
    visible = value
    if (!value) clearTimer()
    else if (attached) void refresh()
  }

  async function create(name: string, seats: string): Promise<boolean> {
    if (state.creating || !attached) return false
    const input = validateClassInput(name, seats)
    update({ fieldErrors: input.errors, message: null, mutationError: null })
    if (!input.value) return false
    const version = lifecycle
    const controller = new AbortController()
    creation = controller
    update({ creating: true })
    try {
      await api.createClass(input.value, { signal: controller.signal })
      if (!attached || version !== lifecycle || controller.signal.aborted) return false
      update({ message: 'Class created.' })
      await refresh()
      if (attached && version === lifecycle && !controller.signal.aborted) {
        update({ creating: false })
        return true
      }
      return false
    } catch (error) {
      if (attached && version === lifecycle && !controller.signal.aborted) {
        const fields = error instanceof ApiError ? error.errors : undefined
        update({ creating: false, mutationError: message(error), fieldErrors: {
          ...(fields?.Name?.length ? { name: fields.Name.join(' ') } : {}),
          ...(fields?.TotalSeats?.length ? { seats: fields.TotalSeats.join(' ') } : {}),
        } })
        if (error instanceof ApiError && error.status === 409) await refresh()
      }
      return false
    }
  }

  async function apply(classId: string): Promise<boolean> {
    const snapshot = student.getSnapshot()
    const row = state.classes.find(item => item.id === classId)
    if (!attached || state.applyingId) return false
    if (!snapshot.studentId) { update({ mutationError: 'Identify a student above before applying.', message: null }); return false }
    if (!row || row.availableSeats <= 0) { update({ mutationError: 'No seat is currently available for this class.', message: null }); return false }
    if (snapshot.applications.some(hold => hold.classId === classId && ['HELD', 'CONFIRMED'].includes(hold.status))) {
      update({ mutationError: 'This student already has an active application for this class.', message: null }); return false
    }
    const version = ++applyVersion
    const controller = new AbortController()
    application = controller
    update({ applyingId: classId, message: null, mutationError: null })
    try {
      await api.createHold(classId, snapshot.studentId, { signal: controller.signal })
      if (!attached || version !== applyVersion || controller.signal.aborted) return false
      update({ applyingId: null, message: 'Application created. A seat is temporarily held for this student.' })
      await Promise.all([refresh(), student.refresh()])
      return true
    } catch (error) {
      if (attached && version === applyVersion && !controller.signal.aborted) {
        update({ applyingId: null, mutationError: message(error) })
        if (error instanceof ApiError && error.status === 409) await Promise.all([refresh(), student.refresh()])
      }
      return false
    }
  }
  return {
    getSnapshot: () => state,
    subscribe: (listener: () => void) => { listeners.add(listener); return () => { listeners.delete(listener) } },
    start, stop, refresh, setVisible, create, apply,
  }
}
