import { useEffect, useRef, useState } from 'react'
import { fetchCitizens, fetchHealth, fetchMap, fetchSettlement, fetchStatus, fetchStructures, type Citizen, type Health, type Map, type Settlement, type Status, type Structure } from './api'

const initialStatus: Status = { state: 'Unknown', worldMinute: null, pendingEventCount: null, worldSeed: null, error: null }
const terrainColors: Record<number, string> = { 1: '#89b8c5', 2: '#d3c78e', 3: '#76966a', 4: '#968873', 5: '#4f785b' }
const structureColors: Record<Structure['type'], string> = { Shelter: '#c76848', Stockpile: '#805c3d', Workshop: '#75569a' }

function formatMinute(minute: number | null) { return minute === null ? '—' : new Intl.NumberFormat('en-US').format(minute) }
function formatValue(value: number | null | undefined) { return value === null || value === undefined ? '—' : new Intl.NumberFormat('en-US').format(value) }
function percent(complete: number, required: number) { return required === 0 ? 100 : Math.min(100, Math.round((complete / required) * 100)) }
function structureLabel(structure: Structure) { return `${structure.type} at (${structure.location.x}, ${structure.location.y})` }

function SettlementMap({ map, structures, citizens }: { map: Map; structures: Structure[]; citizens: Citizen[] }) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  useEffect(() => {
    const canvas = canvasRef.current
    const context = canvas?.getContext('2d')
    if (!canvas || !context) return
    const padding = 20
    const scale = Math.max(2, Math.min(24, Math.floor(Math.min((canvas.width - padding * 2) / map.width, (canvas.height - padding * 2) / map.height))))
    const drawWidth = map.width * scale
    const drawHeight = map.height * scale
    const offsetX = Math.floor((canvas.width - drawWidth) / 2)
    const offsetY = Math.floor((canvas.height - drawHeight) / 2)
    context.fillStyle = '#f7f0e5'; context.fillRect(0, 0, canvas.width, canvas.height)
    for (let y = 0; y < map.height; y += 1) for (let x = 0; x < map.width; x += 1) { context.fillStyle = terrainColors[map.terrain[y * map.width + x]]; context.fillRect(offsetX + x * scale, offsetY + y * scale, scale, scale) }
    context.strokeStyle = '#f7f0e5'; context.lineWidth = Math.max(1, scale / 12); context.strokeRect(offsetX + map.startingSite.x * scale, offsetY + map.startingSite.y * scale, scale, scale)
    for (const structure of structures) {
      const x = offsetX + structure.location.x * scale; const y = offsetY + structure.location.y * scale
      context.fillStyle = structureColors[structure.type]; context.globalAlpha = structure.status === 'Complete' ? 1 : 0.52; context.fillRect(x + scale * 0.18, y + scale * 0.18, scale * 0.64, scale * 0.64); context.globalAlpha = 1
      if (structure.status === 'UnderConstruction') { context.strokeStyle = '#302a24'; context.setLineDash([Math.max(1, scale / 5), Math.max(1, scale / 6)]); context.strokeRect(x + scale * 0.11, y + scale * 0.11, scale * 0.78, scale * 0.78); context.setLineDash([]) }
    }
    context.fillStyle = '#302a24'
    for (const citizen of citizens.filter(entry => entry.isAlive)) context.fillRect(offsetX + citizen.location.x * scale + scale * 0.42, offsetY + citizen.location.y * scale + scale * 0.42, Math.max(2, scale * 0.18), Math.max(2, scale * 0.18))
  }, [citizens, map, structures])
  return <figure className="map-figure" aria-labelledby="map-heading">
    <canvas ref={canvasRef} className="settlement-map" width="760" height="420" role="img" aria-label={`Settlement map, ${map.width} by ${map.height} tiles. The white outline marks the starting site; colored squares mark structures; dark points mark living citizens.`} />
    <figcaption><strong id="map-heading">The settlement grounds</strong><span>Terrain is a presentation of the authoritative map. Starting site is outlined in white.</span></figcaption>
    <ul className="map-legend" aria-label="Map legend"><li><i className="legend-start" />Starting site</li><li><i className="legend-shelter" />Shelter</li><li><i className="legend-stockpile" />Stockpile</li><li><i className="legend-workshop" />Workshop</li><li><i className="legend-citizen" />Citizen</li></ul>
  </figure>
}

export function App() {
  const [health, setHealth] = useState<Health | null>(null)
  const [status, setStatus] = useState(initialStatus)
  const [error, setError] = useState<string | null>(null)
  const [mapError, setMapError] = useState<string | null>(null)
  const [citizens, setCitizens] = useState<Citizen[] | null>(null)
  const [settlement, setSettlement] = useState<Settlement | null>(null)
  const [structures, setStructures] = useState<Structure[] | null>(null)
  const [map, setMap] = useState<Map | null>(null)

  useEffect(() => {
    let active = true
    let timer: number | null = null
    void fetchMap().then(value => { if (active) { setMap(value); setMapError(null) } }).catch((reason: unknown) => { if (active) setMapError(reason instanceof Error ? reason.message : 'The map could not be read.') })
    const load = async () => {
      try {
        const [healthResult, statusResult, citizensResult, settlementResult, structuresResult] = await Promise.allSettled([fetchHealth(), fetchStatus(), fetchCitizens(), fetchSettlement(), fetchStructures()])
        if (healthResult.status === 'rejected') throw healthResult.reason
        if (statusResult.status === 'rejected') throw statusResult.reason
        if (citizensResult.status === 'rejected') throw citizensResult.reason
        if (settlementResult.status === 'rejected') throw settlementResult.reason
        if (structuresResult.status === 'rejected') throw structuresResult.reason
        if (!active) return
        setHealth(healthResult.value); setStatus(statusResult.value); setCitizens(citizensResult.value); setSettlement(settlementResult.value); setStructures(structuresResult.value); setError(null)
      } catch (reason: unknown) {
        if (active) setError(reason instanceof Error ? reason.message : 'The server could not be reached.')
      } finally {
        if (active) timer = window.setTimeout(() => { timer = null; void load() }, 2000)
      }
    }
    void load()
    return () => { active = false; if (timer !== null) window.clearTimeout(timer) }
  }, [])

  const connected = health?.ok === true && error === null
  const stateLabel = status.state.toLowerCase() === 'running' ? 'Running' : status.state
  const activeProject = settlement?.activeConstructionProject
  return <main className="page-shell">
    <div className="topline"><span className="mark" aria-hidden="true">✦</span><span>Observer station</span><span className="rule" /></div>
    <header className="hero"><p className="eyebrow">A world in quiet motion</p><h1>Little <em>Ages</em></h1><p className="intro">A small window onto a persistent world.<br className="desktop-break" /> The server keeps time; this page simply looks in.</p></header>
    <section className="status-strip" aria-label="Connection status" role="status" aria-live="polite"><span className={`status-dot ${connected ? 'is-good' : ''}`} aria-hidden="true" /><span>{error ? 'Connection unavailable' : health === null ? 'Checking the server…' : connected ? 'Server connected' : `Server: ${health.label}`}</span><span className="status-detail">Read-only view</span></section>
    {error && <div className="notice" role="alert">{error}. Make sure the Little Ages server is running.</div>}
    {mapError && <div className="notice" role="alert">Map unavailable: {mapError}.</div>}
    {status.error && <div className="notice" role="alert">Server reports: {status.error}</div>}
    <section className="observation" aria-labelledby="observation-heading"><div className="section-heading"><span className="section-kicker">Current observation</span><h2 id="observation-heading">The world, as it is now</h2></div><div className="reading-grid"><article className="reading reading-primary"><span className="reading-label">World minute</span><strong>{formatMinute(status.worldMinute)}</strong><span className="reading-note">Canonical simulation time</span></article><article className="reading"><span className="reading-label">Host state</span><strong>{health === null ? 'Loading…' : stateLabel}</strong><span className="reading-note">Authoritative server</span></article></div></section>
    <section className="settlement" aria-labelledby="settlement-heading"><div className="section-heading"><span className="section-kicker">Settlement & construction</span><h2 id="settlement-heading">The shared stores</h2></div>
      {settlement === null && !error && <p role="status" aria-live="polite">Loading settlement…</p>}
      {settlement !== null && <><div className="settlement-grid settlement-grid-expanded"><article className="reading reading-primary"><span className="reading-label">Storage</span><strong>{formatValue(settlement.storageUsed)}<small> / {formatValue(settlement.storageCapacity)}</small></strong><span className="reading-note">Used capacity</span></article><article className="reading"><span className="reading-label">Shelter</span><strong>{formatValue(settlement.shelteredPopulation)}<small> / {formatValue(settlement.shelterCapacity)}</small></strong><span className="reading-note">Housed · {formatValue(settlement.unhousedPopulation)} unhoused</span></article><article className="reading"><span className="reading-label">Buildings complete</span><strong>{formatValue(settlement.completedShelters + settlement.completedStockpiles + settlement.completedWorkshops)}</strong><span className="reading-note">{settlement.completedShelters} shelters · {settlement.completedStockpiles} stockpiles · {settlement.completedWorkshops} workshops</span></article><article className="reading"><span className="reading-label">Exposure grace</span><strong>{formatMinute(settlement.exposureGraceUntilMinute)}</strong><span className="reading-note">Until this world minute</span></article></div><div className="stock-grid" aria-label="Settlement resources"><span>Food <strong>{formatValue(settlement.foodStored)}</strong></span><span>Wood <strong>{formatValue(settlement.woodStored)}</strong></span><span>Stone <strong>{formatValue(settlement.stoneStored)}</strong></span></div>
        {activeProject !== null && activeProject !== undefined && <article className="active-project"><div><span className="section-kicker">Active construction</span><h3>{structureLabel(activeProject)}</h3></div><div className="project-progress"><span>Materials: wood {activeProject.deliveredWood}/{activeProject.requiredWood} · stone {activeProject.deliveredStone}/{activeProject.requiredStone}</span><progress value={activeProject.completedWork} max={activeProject.requiredWork} aria-label={`${activeProject.type} work progress`} /><span>{formatValue(activeProject.completedWork)} / {formatValue(activeProject.requiredWork)} work · {percent(activeProject.completedWork, activeProject.requiredWork)}%</span></div></article>}</>}
    </section>
    <section className="structures" aria-labelledby="structures-heading"><div className="section-heading"><span className="section-kicker">Building ledger</span><h2 id="structures-heading">Shelter, stores, and workshop</h2></div>{structures === null && !error && <p role="status" aria-live="polite">Loading structures…</p>}{structures !== null && structures.length === 0 && <p>No structures have been raised.</p>}{structures && structures.length > 0 && <div className="structure-grid">{structures.map(structure => <article className={`structure-card ${structure.status === 'Complete' ? 'is-complete' : ''}`} key={structure.structureId}><div className="structure-card-heading"><h3>{structure.type}</h3><span>{structure.status === 'Complete' ? 'Complete' : 'Building'}</span></div><p>At ({structure.location.x}, {structure.location.y}) · started {formatMinute(structure.startedMinute)}</p><dl className="structure-details"><div><dt>Wood</dt><dd>{formatValue(structure.deliveredWood)} / {formatValue(structure.requiredWood)}</dd></div><div><dt>Stone</dt><dd>{formatValue(structure.deliveredStone)} / {formatValue(structure.requiredStone)}</dd></div><div><dt>Work</dt><dd>{formatValue(structure.completedWork)} / {formatValue(structure.requiredWork)}</dd></div><div><dt>Occupants</dt><dd>{formatValue(structure.currentOccupantIds.length)}</dd></div></dl></article>)}</div>}</section>
    {map !== null && <section className="map-section" aria-labelledby="map-heading"><SettlementMap map={map} structures={structures ?? []} citizens={citizens ?? []} /></section>}
    <section className="citizens" aria-labelledby="citizens-heading"><div className="section-heading"><span className="section-kicker">Citizens</span><h2 id="citizens-heading">Twenty lives in motion</h2></div>{citizens === null && !error && <p role="status" aria-live="polite">Loading citizens…</p>}{citizens !== null && citizens.length === 0 && <p>No citizens are available.</p>}{citizens && citizens.length > 0 && <div className="citizen-grid">{citizens.map(citizen => <article className={`citizen-card ${citizen.isAlive ? '' : 'is-dead'}`} key={citizen.citizenId}><h3>{citizen.name}</h3><p>{citizen.age} years · {citizen.isAlive ? 'Alive' : 'Dead'} · {citizen.occupation}</p><p>At ({citizen.location.x}, {citizen.location.y})</p><dl className="citizen-details"><div><dt>Health</dt><dd>{formatValue(citizen.health)}</dd></div><div><dt>Hunger</dt><dd>{formatValue(citizen.hunger)}</dd></div><div><dt>Rest</dt><dd>{formatValue(citizen.rest)}</dd></div><div><dt>Current action</dt><dd>{citizen.currentAction}</dd></div><div><dt>Phase</dt><dd>{citizen.actionPhase}</dd></div>{citizen.homeStructureId !== null && <div><dt>Home</dt><dd>Structure {citizen.homeStructureId}</dd></div>}{citizen.targetStructureId !== null && <div><dt>Build / haul target</dt><dd>Structure {citizen.targetStructureId}</dd></div>}{citizen.lifetimeWorkActivity.constructionMinutes + citizen.lifetimeWorkActivity.haulingMinutes > 0 && <div><dt>Build / haul work</dt><dd>{formatValue(citizen.lifetimeWorkActivity.constructionMinutes)} / {formatValue(citizen.lifetimeWorkActivity.haulingMinutes)} min</dd></div>}{!citizen.isAlive && <div><dt>Death cause</dt><dd>{citizen.deathCause || 'Unknown'}</dd></div>}{!citizen.isAlive && citizen.deathMinute !== null && <div><dt>Death minute</dt><dd>{formatValue(citizen.deathMinute)}</dd></div>}{citizen.carriedResource !== null && <div><dt>Carrying</dt><dd>{citizen.carriedResource} · {formatValue(citizen.carriedQuantity)}</dd></div>}{citizen.targetResourceNodeId !== null && <div><dt>Target node</dt><dd>{citizen.targetResourceNodeId}</dd></div>}</dl></article>)}</div>}</section>
    <footer><span>Little Ages · Milestone 4</span><span>No controls. No interruptions.</span></footer>
  </main>
}
