import { useLanguage } from './languageContext.ts'
import { useEffect, useRef, useSyncExternalStore } from 'react'
import { formatCountdown } from './holdDetails.ts'
import type { createHoldDetails } from './holdDetails.ts'

const labels = { HELD: 'Seat held', CONFIRMED: 'Confirmed', EXPIRED: 'Expired', CANCELLED: 'Cancelled' }
export function HoldDetailPanel({ controller }: { controller: ReturnType<typeof createHoldDetails> }) {
  const { t, date } = useLanguage()
  const state = useSyncExternalStore(controller.subscribe, controller.getSnapshot, controller.getSnapshot)
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
    <div className="applications-heading"><div><p className="eyebrow">{t("APPLICATION DETAILS")}</p><h2 id="hold-heading">{state.selection.className}</h2></div><button type="button" onClick={controller.close}>{t("Close details")}</button></div>
    {state.details && <div className="hold-overview"><span className={`status status-${state.details.status.toLowerCase()}`}>{t(labels[state.details.status])}</span><p>{t("Application reference")} <span className="reference">{state.details.holdId}</span></p></div>}
    {state.details?.status === 'HELD' && <div className="countdown"><div><p className="eyebrow">{t("TIME TO COMPLETE PAYMENT")}</p><strong aria-label={t("Remaining hold time")}>{formatCountdown(state.remaining ?? 0)}</strong></div><p>{state.checkingExpiry ? t('Time has elapsed. Checking the latest status…') : t('Your seat is temporarily reserved. Complete the simulated payment before the hold expires.')}</p></div>}
    {state.details && <p className="field-help">{t('Original hold deadline')}: <time dateTime={state.details.expiresAt}>{date(state.details.expiresAt)}</time></p>}
    {state.loading && <p role="status" className="muted">{t("Checking application status…")}</p>}
    {state.error && <div role="alert" className="error-panel"><p>{t(state.error)}</p><button type="button" onClick={() => { void controller.refresh() }}>{t("Try checking again")}</button></div>}
    {state.actionError && <p role="alert" className="error-panel">{t(state.actionError)} {t('The latest status is shown here; check it before trying again.')}</p>}
    {state.message && <p role="status" className="success-note">{t(state.message)}</p>}
    {state.details?.status === 'HELD' && <><div className="hold-actions"><button type="button" className="primary" disabled={!allowed} onClick={() => { void controller.confirm() }}>{state.mutation === 'confirm' ? t('Confirming…') : t('Simulate payment & confirm')}</button><button type="button" disabled={!allowed} onClick={() => { void controller.cancel() }}>{state.mutation === 'cancel' ? t('Cancelling…') : t('Cancel application')}</button></div><p className="field-help">{t("This demo simulates a successful payment. No money is charged.")}</p></>}
    {state.details?.status === 'EXPIRED' && <p className="muted">{t("This hold has expired. Check the class list to apply again if a seat is available.")}</p>}
    <button type="button" className="text-button" disabled={state.loading || !!state.mutation} onClick={() => { void controller.refresh() }}>{t("Refresh application status")}</button>
  </section>
}
