import { ApiError } from './api/errors.ts'
import type { AdmissionApi } from './api/client.ts'
import type { HoldDetails, StudentHold } from './api/types.ts'
import type { createStudentSession } from './studentSession.ts'

export interface HoldDetailState {
  selection: StudentHold | null; details: HoldDetails | null; loading: boolean
  remaining: number | null; checkingExpiry: boolean; mutation: 'confirm' | 'cancel' | null
  error: string | null; actionError: string | null; message: string | null; blocked: boolean
}
interface Timing { now: () => number; set: (callback: () => void, milliseconds: number) => unknown; clear: (handle: unknown) => void }
const empty = (): HoldDetailState => ({ selection: null, details: null, loading: false, remaining: null,
  checkingExpiry: false, mutation: null, error: null, actionError: null, message: null, blocked: false })
export function formatCountdown(seconds: number): string {
  const value = Math.max(0, Math.ceil(seconds))
  const minutes = Math.floor(value / 60)
  return `${String(minutes).padStart(2, '0')}:${String(value % 60).padStart(2, '0')}`
}

export function createHoldDetails(api: Pick<AdmissionApi, 'getHold' | 'confirmHold' | 'cancelHold'>,
  student: ReturnType<typeof createStudentSession>, onChanged: () => Promise<unknown>, timing: Timing = {
    now: () => performance.now(), set: (callback, milliseconds) => setTimeout(callback, milliseconds),
    clear: handle => clearTimeout(handle as ReturnType<typeof setTimeout>),
  }) {
  let state = empty()
  const listeners = new Set<() => void>()
  let attached = false, visible = true
  let generation = 0
  let deadline = 0
  let read: AbortController | null = null, write: AbortController | null = null
  let poll: unknown, tick: unknown
  let unsubscribe: (() => void) | null = null
  let selectedStudent: string | null = null
  let missing = false
  function update(patch: Partial<HoldDetailState>) { state = { ...state, ...patch }; for (const listener of listeners) listener() }
  function clearTimer(handle: unknown) { if (handle !== undefined) timing.clear(handle) }
  function clearTimers() { clearTimer(poll); clearTimer(tick); poll = tick = undefined }
  function current(version: number) { return attached && version === generation && selectedStudent === student.getSnapshot().studentId }
  function remainingMs() { return Math.max(0, deadline - timing.now()) }
  function errorText(error: unknown) { return error instanceof ApiError ? error.message : 'Application status could not be checked. Please try again.' }
  function close() {
    generation++; read?.abort(); write?.abort(); clearTimers(); selectedStudent = null; missing = false
    state = empty(); for (const listener of listeners) listener()
  }
  function scheduleTick() {
    clearTimer(tick); tick = undefined
    if (!attached || !visible || state.details?.status !== 'HELD' || remainingMs() <= 0) return
    tick = timing.set(() => {
      tick = undefined
      const remaining = Math.ceil(remainingMs() / 1000)
      update({ remaining, checkingExpiry: remaining === 0 })
      if (remaining === 0) { void refresh(); void onChanged() }
      else scheduleTick()
    }, Math.min(1000, remainingMs()))
  }

  async function refresh(notify = true): Promise<void> {
    if (!attached || !state.selection || state.loading || state.mutation) return
    clearTimer(poll); poll = undefined
    const selection = state.selection
    const version = generation
    const controller = new AbortController()
    read = controller
    const sent = timing.now()
    const previous = state.details?.status
    update({ loading: true })
    try {
      const details = await api.getHold(selection.holdId, { signal: controller.signal })
      if (!current(version) || controller.signal.aborted) return
      const expires = Date.parse(details.expiresAt), server = Date.parse(details.serverTime)
      if (details.holdId !== selection.holdId || details.classId !== selection.classId ||
          !['HELD', 'CONFIRMED', 'EXPIRED', 'CANCELLED'].includes(details.status) || !Number.isFinite(expires) || !Number.isFinite(server)) {
        throw new ApiError('response', 'The server returned an unreadable application status.')
      }
      // Anchor to the send instant, conservatively counting the whole round trip.
      // The browser wall clock never authorizes a payment.
      deadline = sent + Math.max(0, expires - server)
      missing = false
      const remaining = details.status === 'HELD' ? Math.ceil(remainingMs() / 1000) : null
      update({ details, remaining, checkingExpiry: remaining === 0, loading: false, error: null })
      scheduleTick()
      if (notify && previous === 'HELD' && details.status !== previous) await onChanged()
    } catch (error) {
      if (current(version) && !controller.signal.aborted) {
        missing = error instanceof ApiError && error.status === 404
        update({ loading: false, error: errorText(error), ...(missing ? { details: null } : {}) })
      }
    } finally {
      if (current(version) && visible && !missing && (state.details?.status === 'HELD' || !state.details)) {
        poll = timing.set(() => { poll = undefined; void refresh() }, 2500)
      }
    }
  }

  async function open(selection: StudentHold) {
    close()
    selectedStudent = student.getSnapshot().studentId
    if (!attached || !selectedStudent) return
    update({ selection })
    await refresh()
  }
  function start() {
    attached = true
    unsubscribe?.()
    unsubscribe = student.subscribe(() => {
      if (state.selection && selectedStudent !== student.getSnapshot().studentId) close()
    })
  }
  function stop() {
    attached = false; generation++; read?.abort(); write?.abort(); clearTimers(); unsubscribe?.(); unsubscribe = null
    // A remounted view must load fresh details before enabling actions.
    state = empty()
  }
  function setVisible(value: boolean) {
    if (visible === value) return
    visible = value
    if (!value) clearTimers()
    else { update({ remaining: state.details?.status === 'HELD' ? Math.ceil(remainingMs() / 1000) : null }); void refresh() }
  }
  function canAct() {
    return attached && visible && !!selectedStudent && selectedStudent === student.getSnapshot().studentId &&
      state.details?.status === 'HELD' && remainingMs() > 0 && !state.loading && !state.mutation && !state.error && !state.blocked
  }
  async function mutate(action: 'confirm' | 'cancel'): Promise<boolean> {
    if (!canAct() || !state.selection || !selectedStudent) {
      if (state.details?.status === 'HELD' && remainingMs() === 0) {
        update({ remaining: 0, checkingExpiry: true }); void refresh()
      }
      return false
    }
    const version = generation
    const controller = new AbortController()
    write = controller
    const holdId = state.selection.holdId, studentId = selectedStudent
    clearTimers()
    update({ mutation: action, actionError: null, message: null })
    try {
      if (action === 'confirm') await api.confirmHold(holdId, studentId, { signal: controller.signal })
      else await api.cancelHold(holdId, studentId, { signal: controller.signal })
      if (!current(version) || controller.signal.aborted) return false
      update({ mutation: null, details: state.details ? { ...state.details, status: action === 'confirm' ? 'CONFIRMED' : 'CANCELLED' } : null,
        remaining: null, checkingExpiry: false, message: action === 'confirm' ? 'Admission confirmed.' : 'Application cancelled. The seat is released.' })
      await Promise.all([refresh(false), onChanged()])
      return current(version)
    } catch (error) {
      if (!current(version) || controller.signal.aborted) return false
      update({ mutation: null, actionError: errorText(error), blocked: error instanceof ApiError && [403, 404].includes(error.status ?? 0) })
      // A failed response can follow a committed write. Reconcile reads, never retry the mutation.
      await Promise.all([refresh(false), onChanged()])
      return false
    }
  }
  return {
    getSnapshot: () => state,
    subscribe: (listener: () => void) => { listeners.add(listener); return () => { listeners.delete(listener) } },
    start, stop, open, close, refresh, setVisible, canAct,
    confirm: () => mutate('confirm'), cancel: () => mutate('cancel'),
  }
}
