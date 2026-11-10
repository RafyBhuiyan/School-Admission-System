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

export function createStudentSession(api: Pick<AdmissionApi, 'getMyHolds'>, storage: StudentStorage | null) {
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
  function update(next: StudentState) { state = next; for (const listener of listeners) listener() }
  function stop() { generation++; pending?.abort(); pending = null }

  async function refresh() {
    stop()
    const studentId = state.studentId
    if (!studentId) return
    const version = generation
    const controller = new AbortController()
    pending = controller
    update({ ...state, phase: 'loading', error: null })
    try {
      const applications = await api.getMyHolds(studentId, { signal: controller.signal })
      if (generation === version && !controller.signal.aborted) update({ ...state, applications, phase: 'ready', error: null })
    } catch (error) {
      if (generation !== version || controller.signal.aborted) return
      update({ ...state, phase: 'error', error: error instanceof ApiError ? error.message : 'Applications could not be loaded. Please try again.' })
    }
  }

  async function identify(value: string): Promise<boolean> {
    const studentId = validStudentId(value)
    if (!studentId) return false
    stop()
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
    stop()
    let notice: string | null = null
    try {
      if (!storage) throw new Error('Storage unavailable')
      storage.removeItem(STUDENT_STORAGE_KEY)
    }
    catch { notice = 'Saved details could not be cleared. Clear this site’s data before leaving a shared device.' }
    update({ studentId: null, applications: [], phase: 'idle', error: null, notice })
  }

  async function restore() {
    stop()
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
    start: refresh, stop, refresh, identify, forget, changeStudent: forget, restore,
  }
}
