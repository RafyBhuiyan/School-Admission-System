import { ApiError } from './api/errors.ts'
import type { AdmissionApi } from './api/client.ts'
import type { StudentHold } from './api/types.ts'

export const STUDENT_STORAGE_KEY = 'school-admission.student-id'
type StudentStorage = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>
export interface StudentState {
  studentId: string | null
  applications: StudentHold[]
  phase: 'idle' | 'loading' | 'ready' | 'error'
  error: string | null
  notice: string | null
}

export function validStudentId(value: string): string | null {
  const trimmed = value.trim()
  return /^[0-9]{17}$/.test(trimmed) ? trimmed : null
}

export function browserStudentStorage(): StudentStorage | null {
  try { return window.localStorage } catch { return null }
}

interface Scheduler {
  set: (callback: () => void, delay: number) => unknown
  clear: (handle: unknown) => void
}

export function createStudentSession(api: Pick<AdmissionApi, 'getMyHolds'>, storage: StudentStorage | null, scheduler: Scheduler = {
  set: (callback, delay) => setTimeout(callback, delay),
  clear: handle => clearTimeout(handle as ReturnType<typeof setTimeout>),
}) {
  let saved: string | null = null
  let notice: string | null = null
  try {
    const raw = storage?.getItem(STUDENT_STORAGE_KEY)
    if (raw && validStudentId(raw) === raw) saved = raw
  } catch { notice = 'This browser cannot restore a saved student. Enter the identifier to continue.' }
  let state: StudentState = { studentId: saved, applications: [], phase: 'idle', error: null, notice }
  const listeners = new Set<() => void>()
  let generation = 0
  let pending: AbortController | null = null
  let active = false, visible = true
  let poll: unknown
  function update(next: StudentState) { state = next; for (const listener of listeners) listener() }
  function clearPoll() { if (poll !== undefined) scheduler.clear(poll); poll = undefined }
  function invalidate() { generation++; pending?.abort(); pending = null; clearPoll() }
  function stop() { active = false; invalidate() }
  function schedulePoll() {
    clearPoll()
    if (active && visible && state.studentId && state.applications.some(application => application.status === 'HELD')) {
      poll = scheduler.set(() => { poll = undefined; void refresh(true) }, 2500)
    }
  }

  async function refresh(background = false) {
    if (background && pending) return
    invalidate()
    const studentId = state.studentId
    if (!studentId) return
    const version = generation
    const controller = new AbortController()
    pending = controller
    // Keep the list and its controls steady during periodic reads.
    if (!background) update({ ...state, phase: 'loading', error: null })
    try {
      const applications = await api.getMyHolds(studentId, { signal: controller.signal })
      if (generation === version && !controller.signal.aborted) update({ ...state, applications, phase: 'ready', error: null })
    } catch (error) {
      if (generation !== version || controller.signal.aborted) return
      update({ ...state, phase: 'error', error: error instanceof ApiError ? error.message : 'Applications could not be loaded. Please try again.' })
    } finally {
      if (generation === version) { pending = null; schedulePoll() }
    }
  }

  async function identify(value: string): Promise<boolean> {
    const studentId = validStudentId(value)
    if (!studentId) return false
    invalidate()
    let notice: string | null = null
    try {
      if (!storage) throw new Error('Storage unavailable')
      storage.removeItem(STUDENT_STORAGE_KEY)
      storage.setItem(STUDENT_STORAGE_KEY, studentId)
    } catch { notice = 'This browser could not remember the student. Clear this site’s data before leaving a shared device.' }
    update({ studentId, applications: [], phase: 'idle', error: null, notice })
    await refresh()
    return true
  }

  function forget() {
    invalidate()
    let notice: string | null = null
    try {
      if (!storage) throw new Error('Storage unavailable')
      storage.removeItem(STUDENT_STORAGE_KEY)
    }
    catch { notice = 'Saved details could not be cleared. Clear this site’s data before leaving a shared device.' }
    update({ studentId: null, applications: [], phase: 'idle', error: null, notice })
  }

  async function restore() {
    invalidate()
    let studentId: string | null = null
    let notice: string | null = null
    try {
      const raw = storage?.getItem(STUDENT_STORAGE_KEY)
      if (raw && validStudentId(raw) === raw) studentId = raw
    } catch { notice = 'This browser cannot restore a saved student. Enter the identifier to continue.' }
    update({ studentId, applications: [], phase: 'idle', error: null, notice })
    await refresh()
  }

  return {
    getSnapshot: () => state,
    subscribe: (listener: () => void) => { listeners.add(listener); return () => { listeners.delete(listener) } },
    start: () => { active = true; return refresh() }, stop, refresh, identify, forget, changeStudent: forget, restore,
    setVisible: (value: boolean) => {
      if (visible === value) return
      visible = value
      if (!value) clearPoll()
      else if (active && state.studentId) void refresh()
    },
  }
}
