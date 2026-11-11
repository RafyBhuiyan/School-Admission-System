import { useLanguage } from './languageContext.ts'
import { useEffect, useState, useSyncExternalStore } from 'react'
import type { createClassCatalog } from './classCatalog.ts'
import type { StudentHold } from './api/types.ts'
import type { createStudentSession } from './studentSession.ts'

export function ClassPanel({ student, catalog, onApplied }: { student: ReturnType<typeof createStudentSession>;
  catalog: ReturnType<typeof createClassCatalog>; onApplied: (application: StudentHold) => void }) {
  const { t, number } = useLanguage()
  const state = useSyncExternalStore(catalog.subscribe, catalog.getSnapshot, catalog.getSnapshot)
  const identity = useSyncExternalStore(student.subscribe, student.getSnapshot, student.getSnapshot)
  const [search, setSearch] = useState('')
  const [availability, setAvailability] = useState('all')
  const filtered = state.classes.filter(row => row.name.toLowerCase().includes(search.trim().toLowerCase()) && (availability === 'all' || (availability === 'available' ? row.availableSeats > 0 : row.availableSeats === 0)))
  useEffect(() => {
    void catalog.start()
    const visibility = () => catalog.setVisible(document.visibilityState === 'visible')
    visibility()
    document.addEventListener('visibilitychange', visibility)
    return () => { catalog.stop(); document.removeEventListener('visibilitychange', visibility) }
  }, [catalog])
  async function apply(classId: string, className: string) {
    if (await catalog.apply(classId)) {
      const created = catalog.getSnapshot().createdHold
      if (created && created.studentId === student.getSnapshot().studentId) onApplied({ holdId: created.holdId,
        classId: created.classId, className, status: created.status, expiresAt: created.expiresAt })
    }
  }
  return <section className="class-section" aria-labelledby="class-heading">
    <div className="applications-heading"><div><p className="eyebrow">{t("FIND A PLACE")}</p><h2 id="class-heading">{t("Admission classes")}</h2></div><button type="button" disabled={state.loading} onClick={() => { void catalog.refresh() }}>{t("Refresh classes")}</button></div>
    <div className="filter-bar">
      <div><label htmlFor="class-search">{t("Search class or shift")}</label><input id="class-search" type="search" value={search} onChange={event => setSearch(event.target.value)} placeholder={t("e.g. Class 6 or Morning")} /></div>
      <div><label htmlFor="class-availability">{t("Availability")}</label><select id="class-availability" value={availability} onChange={event => setAvailability(event.target.value)}><option value="all">{t("All classes")}</option><option value="available">{t("Seats available")}</option><option value="full">{t("Full classes")}</option></select></div>
      <button type="button" onClick={() => { setSearch(''); setAvailability('all') }}>{t("Clear filters")}</button>
    </div>
    <p className="muted">{t('Showing {shown} of {total} classes', { shown: number(filtered.length), total: number(state.classes.length) })}</p>
    {state.classes.length > 0 && filtered.length === 0 && <div className="empty-state"><h3>{t("No matching classes")}</h3><p>{t("Try another class name or availability filter.")}</p></div>}
    {state.message && <p className="success-note" role="status">{t(state.message)}</p>}
    {state.mutationError && <p className="error-panel" role="alert">{t(state.mutationError)}</p>}
    {state.error && <div className="error-panel" role="alert"><p>{t("Availability could not be refreshed.")} {t(state.error)}</p><button type="button" onClick={() => { void catalog.refresh() }}>{t("Try loading classes again")}</button></div>}
    {state.loading && state.classes.length === 0 && <p className="muted" role="status">{t("Loading classes…")}</p>}
    {!state.loading && !state.error && state.classes.length === 0 && <div className="empty-state"><h3>{t("No admission classes yet")}</h3><p>{t("School administrators can create a class to open applications.")}</p></div>}
    {!identity.studentId && state.classes.length > 0 && <p className="muted">{t("Identify a student above to apply.")}</p>}
    <ul className="class-grid">{filtered.map(row => {
      const existing = identity.applications.some(hold => hold.classId === row.id && ['HELD', 'CONFIRMED'].includes(hold.status))
      const disabled = !identity.studentId || identity.phase === 'loading' || row.availableSeats <= 0 || existing || !!state.applyingId || !!state.error
      return <li key={row.id} className="class-card"><div className="class-card-title"><h3>{row.name}</h3><span className={`status ${row.availableSeats > 0 ? 'status-confirmed' : 'status-expired'}`}>{row.availableSeats > 0 ? t('Seats available') : t('Full')}</span></div>
        <dl className="seat-counts"><div><dt>{t("Total")}</dt><dd>{number(row.totalSeats)}</dd></div><div><dt>{t("Held")}</dt><dd>{number(row.heldSeats)}</dd></div><div><dt>{t("Confirmed")}</dt><dd>{number(row.confirmedSeats)}</dd></div><div className="available-count"><dt>{t("Available")}</dt><dd>{number(row.availableSeats)}</dd></div></dl>
        <button type="button" className="primary" disabled={disabled} aria-label={existing ? t('Already applied for {name}', { name: row.name }) : row.availableSeats <= 0 ? t('No seats available for {name}', { name: row.name }) : t('Apply for {name}', { name: row.name })} onClick={() => { void apply(row.id, row.name) }}>{state.applyingId === row.id ? t('Applying…') : existing ? t('Already applied') : row.availableSeats <= 0 ? t('No seats available') : t('Apply for a seat')}</button>
      </li>
    })}</ul>
    <p className="field-help catalog-note">{t("Availability updates every few seconds. A seat is reserved only after your application succeeds.")}</p>
  </section>
}
