import { LanguageSwitcher } from './LanguageProvider.tsx'
import { useLanguage } from './languageContext.ts'
import { useEffect, useState, useSyncExternalStore } from 'react'
import type { SubmitEvent } from 'react'
import { useApi } from './apiContext.ts'
import { browserStudentStorage, createStudentSession, STUDENT_STORAGE_KEY, validStudentId } from './studentSession.ts'
import './App.css'
import { ClassPanel } from './ClassPanel.tsx'
import { AdminPanel } from './AdminPanel.tsx'
import { createClassCatalog } from './classCatalog.ts'
import { createHoldDetails } from './holdDetails.ts'
import { HoldDetailPanel } from './HoldDetailPanel.tsx'

const statusNames = { HELD: 'Seat held', CONFIRMED: 'Confirmed', EXPIRED: 'Expired', CANCELLED: 'Cancelled' }

function App() {
  const { t, date } = useLanguage()
  const api = useApi()
  const [adminOpen, setAdminOpen] = useState(false)
  const [session] = useState(() => createStudentSession(api, browserStudentStorage()))
  const [catalog] = useState(() => createClassCatalog(api, session))
  const [details] = useState(() => createHoldDetails(api, session, () => Promise.all([catalog.refresh(), session.refresh()])))
  const state = useSyncExternalStore(session.subscribe, session.getSnapshot, session.getSnapshot)
  const [input, setInput] = useState('')
  const [validation, setValidation] = useState<string | null>(null)
  useEffect(() => {
    void session.start()
    const onVisibility = () => session.setVisible(document.visibilityState === 'visible')
    onVisibility()
    const onStorage = (event: StorageEvent) => {
      if (event.key === STUDENT_STORAGE_KEY || event.key === null) void session.restore()
    }
    window.addEventListener('storage', onStorage)
    document.addEventListener('visibilitychange', onVisibility)
    return () => { session.stop(); window.removeEventListener('storage', onStorage); document.removeEventListener('visibilitychange', onVisibility) }
  }, [session])

  function identify(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault()
    const id = validStudentId(input)
    if (!id) { setValidation(t('Enter exactly 17 digits from the birth registration number.')); return }
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
      <header className="site-header"><a className="brand" href="/" aria-label={t("School admissions home")}><span className="brand-mark" aria-hidden="true">S</span><span>{t("School admissions")}<small>{t("Student portal")}</small></span></a><div className="header-actions"><LanguageSwitcher /><button type="button" className="admin-trigger" aria-expanded={adminOpen} aria-controls="admin-panel" onClick={() => setAdminOpen(!adminOpen)}>{t("Admin")}</button></div></header>
      <main>
        {adminOpen && <AdminPanel catalog={catalog} onClose={() => setAdminOpen(false)} />}
        <div className="intro"><p className="eyebrow">{t("ADMISSION APPLICATIONS")}</p><h1>{t("A place to begin.")}</h1><p>{t("Keep track of your student’s applications in one place.")}</p></div>
        {state.notice && <p className="notice" role="status">{t(state.notice)}</p>}
        {!state.studentId ? (
          <section className="identity-card" aria-labelledby="identify-heading">
            <div className="card-heading"><span className="section-mark" aria-hidden="true">ID</span><div><h2 id="identify-heading">{t("Find your student")}</h2><p>{t("Use the 17-digit birth registration number to continue.")}</p></div></div>
            <form onSubmit={identify} noValidate>
              <label htmlFor="student-id">{t("Birth registration number")}</label>
              <input id="student-id" name="student-id" type="text" inputMode="numeric" pattern="[0-9]{17}" autoComplete="off" spellCheck={false} required value={input} onChange={event => { setInput(event.target.value); setValidation(null) }} aria-invalid={!!validation} aria-describedby={validation ? 'student-help student-error' : 'student-help'} placeholder={t("Enter 17 digits")} />
              <p id="student-help" className="field-help">{t("Leading zeros are kept. No password is needed.")}</p>
              {validation && <p id="student-error" className="error" role="alert">{t(validation)}</p>}
              <button className="primary" type="submit">{t("Continue to applications")} <span aria-hidden="true">→</span></button>
            </form>
            <div className="privacy-note"><strong>{t("Using a shared device?")}</strong><p>{t("This browser will remember the student. Choose “Forget this device” when you finish.")}</p></div>
          </section>
        ) : (
          <>
            <section className="student-bar" aria-label={t("Selected student")}><div><p className="eyebrow">{t("CURRENT STUDENT")}</p><h2>{t('Registration ending in {digits}', { digits: state.studentId.slice(-4) })}</h2><p>{state.notice ? t('Selected for this visit. Check the storage notice above.') : t('Remembered on this browser. Forget it when using a shared device.')}</p></div><div className="student-actions"><button type="button" onClick={() => clearStudent(false)}>{t("Change student")}</button><button type="button" className="text-button" onClick={() => clearStudent(true)}>{t("Forget this device")}</button></div></section>
            <HoldDetailPanel controller={details} />
            <section className="applications" aria-labelledby="applications-heading" aria-busy={state.phase === 'loading'}>
              <div className="applications-heading"><div><p className="eyebrow">{t("YOUR PROGRESS")}</p><h2 id="applications-heading">{t("Your applications")}</h2></div><button type="button" disabled={state.phase === 'loading'} onClick={() => { void session.refresh() }}>{t("Refresh")}</button></div>
              {state.applications.some(application => application.status === 'HELD') && <p className="muted">{t("While a seat is held, applications update automatically.")}</p>}
              {state.phase === 'loading' && <p className="muted" role="status">{t("Loading applications…")}</p>}
              {state.error && <div className="error-panel" role="alert"><p>{t(state.error)}</p><button type="button" onClick={() => { void session.refresh() }}>{t("Try again")}</button></div>}
              {state.phase === 'ready' && state.applications.length === 0 && <div className="empty-state"><h3>{t("No applications yet")}</h3><p>{t("Applications for this student will appear here.")}</p></div>}
              {state.applications.length > 0 && <ul className="application-list">{state.applications.map(application => <li key={application.holdId}><div><h3>{application.className}</h3><p>{application.status === 'HELD' ? t('Hold expires') : t('Original hold deadline')}: <time dateTime={application.expiresAt}>{date(application.expiresAt)}</time></p></div><div className="application-actions"><span className={`status status-${application.status.toLowerCase()}`}>{t(statusNames[application.status])}</span><button type="button" onClick={() => { void details.open(application) }} aria-label={t('View application for {name}', { name: application.className })}>{t("View application")}</button></div></li>)}</ul>}
            </section>
          </>
        )}
        <ClassPanel student={session} catalog={catalog} onApplied={application => { void details.open(application) }} />
        <p className="portal-footnote">{t("Application status is confirmed by the school’s admission system.")}</p>
      </main>
      <footer>{t("School admissions")} <span>{t("Simple beginnings. Clear next steps.")}</span></footer>
    </div>
  )
}
export default App
