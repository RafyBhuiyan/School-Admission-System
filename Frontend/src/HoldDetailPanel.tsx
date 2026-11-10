import { useEffect, useRef, useSyncExternalStore } from 'react'
import { formatCountdown } from './holdDetails.ts'
import type { createHoldDetails } from './holdDetails.ts'

const labels = { HELD: 'Seat held', CONFIRMED: 'Confirmed', EXPIRED: 'Expired', CANCELLED: 'Cancelled' }
export function HoldDetailPanel({ controller }: { controller: ReturnType<typeof createHoldDetails> }) {
  const state = useSyncExternalStore(controller.subscribe, controller.getSnapshot)
  const panel = useRef<HTMLElement>(null)
  useEffect(() => {
    controller.start()
    const visibility = () => controller.setVisible(document.visibilityState === 'visible')
    visibility(); document.addEventListener('visibilitychange', visibility)
    return () => { controller.stop(); document.removeEventListener('visibilitychange', visibility) }
  }, [controller])
  useEffect(() => {
    if (state.selection) panel.current?.scrollIntoView({ behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth', block: 'start' })
  }, [state.selection])
  if (!state.selection) return null
  const allowed = controller.canAct()
  return <section className="hold-detail" id="hold-details" ref={panel} aria-labelledby="hold-heading" aria-busy={state.loading || !!state.mutation}>
    <div className="applications-heading"><div><p className="eyebrow">APPLICATION DETAILS</p><h2 id="hold-heading">{state.selection.className}</h2></div><button type="button" onClick={controller.close}>Close details</button></div>
    {state.details && <div className="hold-overview"><span className={`status status-${state.details.status.toLowerCase()}`}>{labels[state.details.status]}</span><p>Application reference <span className="reference">{state.details.holdId}</span></p></div>}
    {state.details?.status === 'HELD' && <div className="countdown"><div><p className="eyebrow">TIME TO COMPLETE PAYMENT</p><strong aria-label="Remaining hold time">{formatCountdown(state.remaining ?? 0)}</strong></div><p>{state.checkingExpiry ? 'Time has elapsed. Checking the latest status…' : 'Your seat is temporarily reserved. Complete the simulated payment before the hold expires.'}</p></div>}
    {state.details && <p className="field-help">Original hold deadline: <time dateTime={state.details.expiresAt}>{new Date(state.details.expiresAt).toLocaleString()}</time></p>}
    {state.loading && <p role="status" className="muted">Checking application status…</p>}
    {state.error && <div role="alert" className="error-panel"><p>{state.error}</p><button type="button" onClick={() => { void controller.refresh() }}>Try checking again</button></div>}
    {state.actionError && <p role="alert" className="error-panel">{state.actionError} The latest status is shown here; check it before trying again.</p>}
    {state.message && <p role="status" className="success-note">{state.message}</p>}
    {state.details?.status === 'HELD' && <><div className="hold-actions"><button type="button" className="primary" disabled={!allowed} onClick={() => { void controller.confirm() }}>{state.mutation === 'confirm' ? 'Confirming…' : 'Simulate payment & confirm'}</button><button type="button" disabled={!allowed} onClick={() => { void controller.cancel() }}>{state.mutation === 'cancel' ? 'Cancelling…' : 'Cancel application'}</button></div><p className="field-help">This demo simulates a successful payment. No money is charged.</p></>}
    {state.details?.status === 'EXPIRED' && <p className="muted">This hold has expired. Check the class list to apply again if a seat is available.</p>}
    <button type="button" className="text-button" disabled={state.loading || !!state.mutation} onClick={() => { void controller.refresh() }}>Refresh application status</button>
  </section>
}
