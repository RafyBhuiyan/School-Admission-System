import { ApiError } from './errors.ts'

export type ResponseShape = 'class' | 'summary' | 'classes' | 'createdHold' | 'hold' | 'confirmedHold' | 'history' | 'empty'
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)
const text = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0
const count = (value: unknown): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
const timestamp = (value: unknown) => typeof value === 'string' && Number.isFinite(Date.parse(value))
const status = (value: unknown) => ['HELD', 'CONFIRMED', 'EXPIRED', 'CANCELLED'].includes(value as string)
const admissionClass = (value: unknown) => record(value) && text(value.id) && text(value.name) && count(value.totalSeats) && value.totalSeats > 0
const summary = (value: unknown) => admissionClass(value) && record(value) && count(value.heldSeats) && count(value.confirmedSeats) && count(value.availableSeats)
const hold = (value: unknown) => record(value) && text(value.holdId) && text(value.classId) && status(value.status) && timestamp(value.expiresAt)

export function validateResponse(shape: ResponseShape, value: unknown): void {
  const valid = shape === 'class' ? admissionClass(value)
    : shape === 'summary' ? summary(value)
    : shape === 'classes' ? Array.isArray(value) && value.every(summary)
    : shape === 'hold' ? hold(value) && record(value) && timestamp(value.serverTime)
    : shape === 'createdHold' ? hold(value) && record(value) && value.status === 'HELD' && text(value.studentId) && value.studentId.length <= 64
    : shape === 'confirmedHold' ? record(value) && text(value.holdId) && value.status === 'CONFIRMED'
    : shape === 'history' ? Array.isArray(value) && value.every(row => hold(row) && record(row) && text(row.className))
    : false
  if (!valid) throw new ApiError('response', 'The server returned an unreadable response. Please try again.')
}
