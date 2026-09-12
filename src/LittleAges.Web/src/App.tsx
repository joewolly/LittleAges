import { useEffect, useState } from 'react'
import { fetchHealth, fetchStatus, type Health, type Status } from './api'

const initialStatus: Status = { state: 'Unknown', worldMinute: null, pendingEventCount: null, worldSeed: null, error: null }

function formatMinute(minute: number | null) {
  return minute === null ? '—' : new Intl.NumberFormat('en-US').format(minute)
}

export function App() {
  const [health, setHealth] = useState<Health | null>(null)
  const [status, setStatus] = useState(initialStatus)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let active = true
    Promise.all([fetchHealth(), fetchStatus()]).then(([nextHealth, nextStatus]) => {
      if (!active) return
      setHealth(nextHealth); setStatus(nextStatus); setError(null)
    }).catch((reason: unknown) => {
      if (active) setError(reason instanceof Error ? reason.message : 'The server could not be reached.')
    })
    return () => { active = false }
  }, [])

  const connected = health?.ok === true && error === null
  const stateLabel = status.state.toLowerCase() === 'running' ? 'Running' : status.state

  return <main className="page-shell">
    <div className="topline"><span className="mark" aria-hidden="true">✦</span><span>Observer station</span><span className="rule" /></div>
    <header className="hero">
      <p className="eyebrow">A world in quiet motion</p>
      <h1>Little <em>Ages</em></h1>
      <p className="intro">A small window onto a persistent world.<br className="desktop-break" /> The server keeps time; this page simply looks in.</p>
    </header>

    <section className="status-strip" aria-label="Connection status" role="status" aria-live="polite">
      <span className={`status-dot ${connected ? 'is-good' : ''}`} aria-hidden="true" />
      <span>{error ? 'Connection unavailable' : health === null ? 'Checking the server…' : connected ? 'Server connected' : `Server: ${health.label}`}</span>
      <span className="status-detail">Read-only view</span>
    </section>

    {error && <div className="notice" role="alert">{error}. Make sure the Little Ages server is running.</div>}
    {status.error && <div className="notice" role="alert">Server reports: {status.error}</div>}

    <section className="observation" aria-labelledby="observation-heading">
      <div className="section-heading"><span className="section-kicker">Current observation</span><h2 id="observation-heading">The world, as it is now</h2></div>
      <div className="reading-grid">
        <article className="reading reading-primary"><span className="reading-label">World minute</span><strong>{formatMinute(status.worldMinute)}</strong><span className="reading-note">Canonical simulation time</span></article>
        <article className="reading"><span className="reading-label">Host state</span><strong>{health === null ? 'Loading…' : stateLabel}</strong><span className="reading-note">Authoritative server</span></article>
      </div>
    </section>

    <footer><span>Little Ages · Milestone 0</span><span>No controls. No interruptions.</span></footer>
  </main>
}
