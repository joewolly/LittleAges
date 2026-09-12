import { useEffect, useState } from 'react'
import { fetchCitizens, fetchHealth, fetchStatus, type Citizen, type Health, type Status } from './api'

const initialStatus: Status = { state: 'Unknown', worldMinute: null, pendingEventCount: null, worldSeed: null, error: null }

function formatMinute(minute: number | null) {
  return minute === null ? '—' : new Intl.NumberFormat('en-US').format(minute)
}

export function App() {
  const [health, setHealth] = useState<Health | null>(null)
  const [status, setStatus] = useState(initialStatus)
  const [error, setError] = useState<string | null>(null)
  const [citizens, setCitizens] = useState<Citizen[] | null>(null)

  useEffect(() => {
    let active = true
    const load = () => Promise.all([fetchHealth(), fetchStatus(), fetchCitizens()]).then(([nextHealth, nextStatus, nextCitizens]) => {
      if (!active) return
      setHealth(nextHealth); setStatus(nextStatus); setCitizens(nextCitizens); setError(null)
    }).catch((reason: unknown) => {
      if (active) setError(reason instanceof Error ? reason.message : 'The server could not be reached.')
    })
    void load()
    const timer = window.setInterval(load, 2000)
    return () => { active = false; window.clearInterval(timer) }
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

    <section className="citizens" aria-labelledby="citizens-heading">
      <div className="section-heading"><span className="section-kicker">Citizens</span><h2 id="citizens-heading">Twenty lives in motion</h2></div>
      {citizens === null && !error && <p role="status" aria-live="polite">Loading citizens…</p>}
      {citizens !== null && citizens.length === 0 && <p>No citizens are available.</p>}
      {citizens && citizens.length > 0 && <div className="citizen-grid">{citizens.map(citizen => <article className="citizen-card" key={citizen.citizenId}><h3>{citizen.name}</h3><p>{citizen.age} years · {citizen.currentAction}</p><p>At ({citizen.location.x}, {citizen.location.y})</p></article>)}</div>}
    </section>

    <footer><span>Little Ages · Milestone 2</span><span>No controls. No interruptions.</span></footer>
  </main>
}
