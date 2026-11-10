import { useEffect, useState, useSyncExternalStore } from 'react'
import type { SubmitEvent } from 'react'
import type { createClassCatalog } from './classCatalog.ts'
import type { StudentHold } from './api/types.ts'
import type { createStudentSession } from './studentSession.ts'

export function ClassPanel({ student, catalog, onApplied }: { student: ReturnType<typeof createStudentSession>;
  catalog: ReturnType<typeof createClassCatalog>; onApplied: (application: StudentHold) => void }) {
  const state = useSyncExternalStore(catalog.subscribe, catalog.getSnapshot)
  const identity = useSyncExternalStore(student.subscribe, student.getSnapshot)
  const [adminOpen, setAdminOpen] = useState(false)
  const [name, setName] = useState('')
  const [seats, setSeats] = useState('')
  useEffect(() => {
    void catalog.start()
    const visibility = () => catalog.setVisible(document.visibilityState === 'visible')
    visibility()
    document.addEventListener('visibilitychange', visibility)
    return () => { catalog.stop(); document.removeEventListener('visibilitychange', visibility) }
  }, [catalog])
  async function submit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault()
    if (await catalog.create(name, seats)) { setName(''); setSeats('') }
  }
  async function apply(classId: string, className: string) {
    if (await catalog.apply(classId)) {
      const created = catalog.getSnapshot().createdHold
      if (created && created.studentId === student.getSnapshot().studentId) onApplied({ holdId: created.holdId,
        classId: created.classId, className, status: created.status, expiresAt: created.expiresAt })
    }
  }
  return <section className="class-section" aria-labelledby="class-heading">
    <div className="applications-heading"><div><p className="eyebrow">FIND A PLACE</p><h2 id="class-heading">Admission classes</h2></div><div className="catalog-actions"><button type="button" disabled={state.loading} onClick={() => { void catalog.refresh() }}>Refresh classes</button><button type="button" aria-expanded={adminOpen} aria-controls="create-class" onClick={() => setAdminOpen(!adminOpen)}>{adminOpen ? 'Close class form' : 'Create a class'}</button></div></div>
    {adminOpen && <form id="create-class" className="class-form" onSubmit={event => { void submit(event) }} noValidate>
      <div className="class-form-heading"><h3>Create an admission class</h3><p>School administration · demo access</p></div>
      <div><label htmlFor="class-name">Class / shift name</label><input id="class-name" value={name} onChange={event => setName(event.target.value)} placeholder="Class 6 – Morning shift" required aria-invalid={!!state.fieldErrors.name} aria-describedby={state.fieldErrors.name ? 'class-name-error' : undefined} disabled={state.creating} />{state.fieldErrors.name && <p id="class-name-error" className="form-error">{state.fieldErrors.name}</p>}</div>
      <div><label htmlFor="class-seats">Total seats</label><input id="class-seats" type="text" inputMode="numeric" value={seats} onChange={event => setSeats(event.target.value)} placeholder="30" required aria-invalid={!!state.fieldErrors.seats} aria-describedby={state.fieldErrors.seats ? 'class-seats-error' : undefined} disabled={state.creating} />{state.fieldErrors.seats && <p id="class-seats-error" className="form-error">{state.fieldErrors.seats}</p>}</div>
      <button className="primary" type="submit" disabled={state.creating}>{state.creating ? 'Creating…' : 'Create class'}</button>
    </form>}
    {state.message && <p className="success-note" role="status">{state.message}</p>}
    {state.mutationError && <p className="error-panel" role="alert">{state.mutationError}</p>}
    {state.error && <div className="error-panel" role="alert"><p>Availability could not be refreshed. {state.error}</p><button type="button" onClick={() => { void catalog.refresh() }}>Try loading classes again</button></div>}
    {state.loading && state.classes.length === 0 && <p className="muted" role="status">Loading classes…</p>}
    {!state.loading && !state.error && state.classes.length === 0 && <div className="empty-state"><h3>No admission classes yet</h3><p>School administrators can create a class to open applications.</p></div>}
    {!identity.studentId && state.classes.length > 0 && <p className="muted">Identify a student above to apply.</p>}
    <ul className="class-grid">{state.classes.map(row => {
      const existing = identity.applications.some(hold => hold.classId === row.id && ['HELD', 'CONFIRMED'].includes(hold.status))
      const disabled = !identity.studentId || identity.phase === 'loading' || row.availableSeats <= 0 || existing || !!state.applyingId || !!state.error
      return <li key={row.id} className="class-card"><div className="class-card-title"><h3>{row.name}</h3><span className={`status ${row.availableSeats > 0 ? 'status-confirmed' : 'status-expired'}`}>{row.availableSeats > 0 ? 'Seats available' : 'Full'}</span></div>
        <dl className="seat-counts"><div><dt>Total</dt><dd>{row.totalSeats}</dd></div><div><dt>Held</dt><dd>{row.heldSeats}</dd></div><div><dt>Confirmed</dt><dd>{row.confirmedSeats}</dd></div><div className="available-count"><dt>Available</dt><dd>{row.availableSeats}</dd></div></dl>
        <button type="button" className="primary" disabled={disabled} aria-label={existing ? `Already applied for ${row.name}` : row.availableSeats <= 0 ? `No seats available for ${row.name}` : `Apply for ${row.name}`} onClick={() => { void apply(row.id, row.name) }}>{state.applyingId === row.id ? 'Applying…' : existing ? 'Already applied' : row.availableSeats <= 0 ? 'No seats available' : 'Apply for a seat'}</button>
      </li>
    })}</ul>
    <p className="field-help catalog-note">Availability updates every few seconds. A seat is reserved only after your application succeeds.</p>
  </section>
}
