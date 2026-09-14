import { useEffect, useState } from 'react'
import { fetchCitizens, fetchHealth, fetchSettlement, fetchStatus, type Citizen, type Health, type Settlement, type Status } from './api'

const initialStatus: Status = { state: 'Unknown', worldMinute: null, pendingEventCount: null, worldSeed: null, error: null }

function formatMinute(minute: number | null) {
  return minute === null ? '—' : new Intl.NumberFormat('en-US').format(minute)
}

function formatValue(value: number | null | undefined) {
  return value === null || value === undefined ? '—' : new Intl.NumberFormat('en-US').format(value)
}

export function App() {
  const [health, setHealth] = useState<Health | null>(null)
  const [status, setStatus] = useState(initialStatus)
  const [error, setError] = useState<string | null>(null)
  const [citizens, setCitizens] = useState<Citizen[] | null>(null)
  const [settlement, setSettlement] = useState<Settlement | null>(null)

  useEffect(() => {
    let active = true
    let timer: number | null = null
    const load = async () => {
      try {
        const [healthResult, statusResult, citizensResult, settlementResult] = await Promise.allSettled([fetchHealth(), fetchStatus(), fetchCitizens(), fetchSettlement()])
        if (healthResult.status === 'rejected') throw healthResult.reason
        if (statusResult.status === 'rejected') throw statusResult.reason
        if (citizensResult.status === 'rejected') throw citizensResult.reason
        if (settlementResult.status === 'rejected') throw settlementResult.reason
        if (!active) return
        setHealth(healthResult.value); setStatus(statusResult.value); setCitizens(citizensResult.value); setSettlement(settlementResult.value); setError(null)
      } catch (reason: unknown) {
        if (active) setError(reason instanceof Error ? reason.message : 'The server could not be reached.')
      } finally {
        if (active) {
          timer = window.setTimeout(() => {
            timer = null
            void load()
          }, 2000)
        }
      }
    }
    void load()
    return () => {
      active = false
      if (timer !== null) window.clearTimeout(timer)
    }
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

    <section className="settlement" aria-labelledby="settlement-heading">
      <div className="section-heading"><span className="section-kicker">Settlement survival</span><h2 id="settlement-heading">The shared stores</h2></div>
      {settlement === null && !error && <p role="status" aria-live="polite">Loading settlement…</p>}
      {settlement !== null && <div className="settlement-grid">
        <article className="reading reading-primary"><span className="reading-label">Living population</span><strong>{formatValue(settlement.livingPopulation)}</strong><span className="reading-note">Citizens still alive</span></article>
        <article className="reading"><span className="reading-label">Deaths</span><strong>{formatValue(settlement.deadPopulation)}</strong><span className="reading-note">Recorded deaths</span></article>
        <article className="reading"><span className="reading-label">Food</span><strong>{formatValue(settlement.foodStored)}</strong><span className="reading-note">Shared stockpile</span></article>
        <article className="reading"><span className="reading-label">Wood</span><strong>{formatValue(settlement.woodStored)}</strong><span className="reading-note">Shared stockpile</span></article>
        <article className="reading"><span className="reading-label">Stone</span><strong>{formatValue(settlement.stoneStored)}</strong><span className="reading-note">Shared stockpile</span></article>
      </div>}
    </section>

    <section className="citizens" aria-labelledby="citizens-heading">
      <div className="section-heading"><span className="section-kicker">Citizens</span><h2 id="citizens-heading">Twenty lives in motion</h2></div>
      {citizens === null && !error && <p role="status" aria-live="polite">Loading citizens…</p>}
      {citizens !== null && citizens.length === 0 && <p>No citizens are available.</p>}
      {citizens && citizens.length > 0 && <div className="citizen-grid">{citizens.map(citizen => <article className={`citizen-card ${citizen.isAlive ? '' : 'is-dead'}`} key={citizen.citizenId}>
        <h3>{citizen.name}</h3>
        <p>{citizen.age} years · {citizen.isAlive ? 'Alive' : 'Dead'}</p>
        <p>At ({citizen.location.x}, {citizen.location.y})</p>
        <dl className="citizen-details">
          <div><dt>Health</dt><dd>{formatValue(citizen.health)}</dd></div>
          <div><dt>Hunger</dt><dd>{formatValue(citizen.hunger)}</dd></div>
          <div><dt>Rest</dt><dd>{formatValue(citizen.rest)}</dd></div>
          <div><dt>Current action</dt><dd>{citizen.currentAction}</dd></div>
          <div><dt>Phase</dt><dd>{citizen.actionPhase}</dd></div>
          {!citizen.isAlive && <div><dt>Death cause</dt><dd>{citizen.deathCause || 'Unknown'}</dd></div>}
          {!citizen.isAlive && citizen.deathMinute !== null && <div><dt>Death minute</dt><dd>{formatValue(citizen.deathMinute)}</dd></div>}
          {citizen.carriedResource !== null && <div><dt>Carrying</dt><dd>{citizen.carriedResource} · {formatValue(citizen.carriedQuantity)}</dd></div>}
          {citizen.targetResourceNodeId !== null && <div><dt>Target node</dt><dd>{citizen.targetResourceNodeId}</dd></div>}
        </dl>
      </article>)}</div>}
    </section>

    <footer><span>Little Ages · Milestone 3</span><span>No controls. No interruptions.</span></footer>
  </main>
}
