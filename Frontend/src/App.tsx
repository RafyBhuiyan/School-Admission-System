import { useEffect, useState, useSyncExternalStore } from 'react'
import type { SubmitEvent } from 'react'
import { useApi } from './apiContext.ts'
import { browserStudentStorage, createStudentSession, STUDENT_STORAGE_KEY, validStudentId } from './studentSession.ts'
import './App.css'
import { ClassPanel } from './ClassPanel.tsx'

const statusNames = { HELD: 'Seat held', CONFIRMED: 'Confirmed', EXPIRED: 'Expired', CANCELLED: 'Cancelled' }

function App() {
  const api = useApi()
  const [session] = useState(() => createStudentSession(api, browserStudentStorage()))
  const state = useSyncExternalStore(session.subscribe, session.getSnapshot)
  const [input, setInput] = useState('')
  const [validation, setValidation] = useState<string | null>(null)
  useEffect(() => {
    void session.start()
    const onStorage = (event: StorageEvent) => {
      if (event.key === STUDENT_STORAGE_KEY || event.key === null) void session.restore()
    }
    window.addEventListener('storage', onStorage)
    return () => { session.stop(); window.removeEventListener('storage', onStorage) }
  }, [session])

  function identify(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault()
    const id = validStudentId(input)
    if (!id) { setValidation('Enter exactly 17 digits from the birth registration number.'); return }
    setValidation(null)
    setInput('')
    void session.identify(id)
  }
  function clearStudent(forget: boolean) {
    setInput('')
    setValidation(null)
    if (forget) session.forget()
    else session.changeStudent()
  }

  return (
    <div className="portal">
      <header className="site-header"><a className="brand" href="/" aria-label="School admissions home"><span className="brand-mark" aria-hidden="true">S</span><span>School admissions<small>Student portal</small></span></a><span className="header-note">Your next chapter starts here</span></header>
      <main>
        <div className="intro"><p className="eyebrow">ADMISSION APPLICATIONS</p><h1>A place to begin.</h1><p>Keep track of your student’s applications in one place.</p></div>
        {state.notice && <p className="notice" role="status">{state.notice}</p>}
        {!state.studentId ? (
          <section className="identity-card" aria-labelledby="identify-heading">
            <div className="card-heading"><span className="section-mark" aria-hidden="true">ID</span><div><h2 id="identify-heading">Find your student</h2><p>Use the 17-digit birth registration number to continue.</p></div></div>
            <form onSubmit={identify} noValidate>
              <label htmlFor="student-id">Birth registration number</label>
              <input id="student-id" name="student-id" type="text" inputMode="numeric" pattern="[0-9]{17}" autoComplete="off" spellCheck={false} required value={input} onChange={event => { setInput(event.target.value); setValidation(null) }} aria-invalid={!!validation} aria-describedby={validation ? 'student-help student-error' : 'student-help'} placeholder="Enter 17 digits" />
              <p id="student-help" className="field-help">Leading zeros are kept. No password is needed.</p>
              {validation && <p id="student-error" className="error" role="alert">{validation}</p>}
              <button className="primary" type="submit">Continue to applications <span aria-hidden="true">→</span></button>
            </form>
            <div className="privacy-note"><strong>Using a shared device?</strong><p>This browser will remember the student. Choose “Forget this device” when you finish.</p></div>
          </section>
        ) : (
          <>
            <section className="student-bar" aria-label="Selected student"><div><p className="eyebrow">CURRENT STUDENT</p><h2>Registration ending in {state.studentId.slice(-4)}</h2><p>{state.notice ? 'Selected for this visit. Check the storage notice above.' : 'Remembered on this browser. Forget it when using a shared device.'}</p></div><div className="student-actions"><button type="button" onClick={() => clearStudent(false)}>Change student</button><button type="button" className="text-button" onClick={() => clearStudent(true)}>Forget this device</button></div></section>
            <section className="applications" aria-labelledby="applications-heading" aria-busy={state.phase === 'loading'}>
              <div className="applications-heading"><div><p className="eyebrow">YOUR PROGRESS</p><h2 id="applications-heading">Your applications</h2></div><button type="button" disabled={state.phase === 'loading'} onClick={() => { void session.refresh() }}>Refresh</button></div>
              {state.phase === 'loading' && <p className="muted" role="status">Loading applications…</p>}
              {state.error && <div className="error-panel" role="alert"><p>{state.error}</p><button type="button" onClick={() => { void session.refresh() }}>Try again</button></div>}
              {state.phase === 'ready' && state.applications.length === 0 && <div className="empty-state"><h3>No applications yet</h3><p>Applications for this student will appear here.</p></div>}
              {state.applications.length > 0 && <ul className="application-list">{state.applications.map(application => <li key={application.holdId}><div><h3>{application.className}</h3><p>{application.status === 'HELD' ? 'Hold expires' : 'Original hold deadline'}: <time dateTime={application.expiresAt}>{new Date(application.expiresAt).toLocaleString()}</time></p></div><span className={`status status-${application.status.toLowerCase()}`}>{statusNames[application.status]}</span></li>)}</ul>}
            </section>
          </>
        )}
        <ClassPanel student={session} />
        <p className="portal-footnote">Application status is confirmed by the school’s admission system.</p>
      </main>
      <footer>School admissions <span>Simple beginnings. Clear next steps.</span></footer>
    </div>
  )
}
export default App
