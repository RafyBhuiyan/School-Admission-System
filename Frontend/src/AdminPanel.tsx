import { useCallback, useEffect, useState, useSyncExternalStore } from 'react'
import type { SubmitEvent } from 'react'
import { useApi } from './apiContext.ts'
import type { createClassCatalog } from './classCatalog.ts'
import type { AdminApplications, HoldStatus } from './api/types.ts'
import { ApiError } from './api/errors.ts'

type Catalog = ReturnType<typeof createClassCatalog>
export function AdminPanel({ catalog, onClose }: { catalog: Catalog; onClose: () => void }) {
  const api = useApi()
  const [key, setKey] = useState('')
  const [code, setCode] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [request, setRequest] = useState<AbortController | null>(null)
  const reject = useCallback(() => { setKey(''); setCode(''); setError('Your admin access has expired. Enter the current code.') }, [])
  useEffect(() => () => request?.abort(), [request])
  async function signIn(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault()
    if (busy) return
    const controller = new AbortController()
    setRequest(controller); setBusy(true); setError('')
    try {
      await api.verifyAdmin(code, { signal: controller.signal })
      if (!controller.signal.aborted) { setKey(code); setCode('') }
    } catch { if (!controller.signal.aborted) setError('Code not accepted or service unavailable. Check your code and try again.') }
    finally { if (!controller.signal.aborted) setBusy(false) }
  }
  return <section id="admin-panel" className="admin-panel" aria-labelledby="admin-heading">
    <div className="applications-heading"><div><p className="eyebrow">SCHOOL OFFICE</p><h2 id="admin-heading">Administration</h2></div><button type="button" onClick={onClose}>{key ? 'Sign out & close' : 'Close'}</button></div>
    {!key ? <form className="admin-login" onSubmit={event => { void signIn(event) }}>
      <p>Create admission classes and review student applications.</p>
      <label htmlFor="admin-code">Admin code</label><input id="admin-code" type="password" autoComplete="off" required maxLength={512} value={code} onChange={event => setCode(event.target.value)} disabled={busy} />
      {error && <p className="form-error" role="alert">{error}</p>}
      <button type="submit" className="primary" disabled={busy}>{busy ? 'Checking code…' : 'Continue'}</button>
      <p className="field-help">For school administrators. Access ends when you close this panel or reload.</p>
    </form> : <AdminWorkspace adminKey={key} catalog={catalog} onRejected={reject} />}
  </section>
}

function AdminWorkspace({ adminKey, catalog, onRejected }: { adminKey: string; catalog: Catalog; onRejected: () => void }) {
  const api = useApi()
  const state = useSyncExternalStore(catalog.subscribe, catalog.getSnapshot)
  const [name, setName] = useState('')
  const [seats, setSeats] = useState('')
  const [classId, setClassId] = useState('')
  const [status, setStatus] = useState<HoldStatus | ''>('')
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [result, setResult] = useState<AdminApplications | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  function resetView() { setLoading(true); setResult(null); setError('') }
  useEffect(() => {
    const controller = new AbortController()
    let timer: ReturnType<typeof setTimeout>
    async function load() {
      try {
        const data = await api.getAdminApplications(adminKey, { classId: classId || undefined, status: status || undefined, page }, { signal: controller.signal })
        if (controller.signal.aborted) return
        setResult(data); setError('')
      } catch (failure) {
        if (controller.signal.aborted) return
        if (failure instanceof ApiError && [401, 403].includes(failure.status ?? 0)) { onRejected(); return }
        setError('Applications could not be refreshed. Please try again.')
      } finally {
        if (!controller.signal.aborted) {
          setLoading(false)
          timer = setTimeout(pollWhenVisible, 4000)
        }
      }
    }
    function pollWhenVisible() {
      if (controller.signal.aborted) return
      if (document.visibilityState === 'visible') void load()
      else timer = setTimeout(pollWhenVisible, 4000)
    }
    void load()
    return () => { controller.abort(); clearTimeout(timer) }
  }, [api, adminKey, classId, status, page, reload, onRejected])
  async function create(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault()
    if (await catalog.create(name, seats, adminKey)) { setName(''); setSeats(''); setReload(value => value + 1) }
  }
  return <>
    <form className="class-form" onSubmit={event => { void create(event) }} noValidate>
      <div className="class-form-heading"><h3>Add an admission class</h3><p>Set the grade, shift and number of available places.</p></div>
      <div><label htmlFor="admin-class-name">Class / shift name</label><input id="admin-class-name" value={name} onChange={event => setName(event.target.value)} placeholder="Class 6 – Morning shift" disabled={state.creating} aria-invalid={!!state.fieldErrors.name} />{state.fieldErrors.name && <p className="form-error">{state.fieldErrors.name}</p>}</div>
      <div><label htmlFor="admin-class-seats">Total seats</label><input id="admin-class-seats" inputMode="numeric" value={seats} onChange={event => setSeats(event.target.value)} placeholder="30" disabled={state.creating} aria-invalid={!!state.fieldErrors.seats} />{state.fieldErrors.seats && <p className="form-error">{state.fieldErrors.seats}</p>}</div>
      <button type="submit" className="primary" disabled={state.creating}>{state.creating ? 'Adding…' : 'Add admission class'}</button>
      {state.message && <p role="status" className="success-note">{state.message}</p>}{state.mutationError && <p role="alert" className="form-error">{state.mutationError}</p>}
    </form>
    <div className="applications-heading"><div><p className="eyebrow">APPLICATION REGISTER</p><h3>Student applications</h3></div><button type="button" disabled={loading} onClick={() => { resetView(); setReload(value => value + 1) }}>Refresh applications</button></div>
    <div className="filter-bar"><div><label htmlFor="admin-class-filter">Admission class</label><select id="admin-class-filter" value={classId} onChange={event => { resetView(); setClassId(event.target.value); setPage(1) }}><option value="">All classes</option>{state.classes.map(row => <option key={row.id} value={row.id}>{row.name}</option>)}</select></div><div><label htmlFor="admin-status-filter">Application status</label><select id="admin-status-filter" value={status} onChange={event => { resetView(); setStatus(event.target.value as HoldStatus | ''); setPage(1) }}><option value="">All statuses</option><option value="HELD">Seat held</option><option value="CONFIRMED">Confirmed</option><option value="EXPIRED">Expired</option><option value="CANCELLED">Cancelled</option></select></div><button type="button" onClick={() => { setReload(value => value + 1); resetView(); setClassId(''); setStatus(''); setPage(1) }}>Clear filters</button></div>
    {error && <p role="alert" className="error-panel">{error}</p>}
    {loading && <p role="status" className="muted">Loading applications…</p>}
    {result && <><p className="muted">{result.total} matching applications · newest first</p><div className="table-scroll"><table className="admin-table"><caption className="sr-only">Applications matching the selected filters</caption><thead><tr><th>Student ID</th><th>Class / shift</th><th>Status</th><th>Applied</th><th>Hold deadline</th></tr></thead><tbody>{result.items.map(row => <tr key={row.holdId}><td className="student-number">{row.studentId}</td><td>{row.className}</td><td><span className={`status status-${row.status.toLowerCase()}`}>{row.status}</span></td><td>{new Date(row.createdAt).toLocaleString()}</td><td>{new Date(row.expiresAt).toLocaleString()}</td></tr>)}</tbody></table></div>{result.items.length === 0 && <div className="empty-state"><h3>No matching applications</h3><p>Try another class or status filter.</p></div>}<div className="pagination"><button type="button" disabled={page <= 1 || loading} onClick={() => { resetView(); setPage(value => value - 1) }}>Previous</button><span>Page {page} of {Math.max(1, Math.ceil(result.total / result.pageSize))}</span><button type="button" disabled={page * result.pageSize >= result.total || loading} onClick={() => { resetView(); setPage(value => value + 1) }}>Next</button></div></>}
  </>
}
