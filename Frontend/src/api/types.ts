export type HoldStatus = 'HELD' | 'CONFIRMED' | 'EXPIRED' | 'CANCELLED'
export interface CreateClassRequest { name: string; totalSeats: number }
export interface AdmissionClass { id: string; name: string; totalSeats: number }
export interface ClassSummary extends AdmissionClass { heldSeats: number; confirmedSeats: number; availableSeats: number }
export interface CreatedHold { holdId: string; classId: string; studentId: string; status: 'HELD'; expiresAt: string }
export interface HoldDetails { holdId: string; classId: string; status: HoldStatus; expiresAt: string; serverTime: string }
export interface ConfirmedHold { holdId: string; status: 'CONFIRMED' }
export interface StudentHold { holdId: string; classId: string; className: string; status: HoldStatus; expiresAt: string }
