import { isCitizenPresent } from '../newcomers'
import { Component, useEffect, useState, type ReactNode } from 'react'
import type { Citizen, Map, RoadOverlay, Settlement, SettlementSite, Structure } from '../api'
import type { LivingWorld } from '../living'
import { HudIcon, ResourceMeters, SettlementBadge, VillagerCard } from './Hud'
import { IsoWorld, type CameraNudge, type IsoStats } from './IsoWorld'
import { villagerSprite } from './iso/scene'
import type { Season } from './iso/seasons'
import { LegacyMap } from './LegacyMap'
import { citizenActivity } from './presentation'

const diagnosticsEnabled = import.meta.env.DEV || new URLSearchParams(window.location.search).has('diagnostics')

function supportsCanvas(): boolean {
  if (typeof document === 'undefined' || typeof navigator === 'undefined' || /jsdom/i.test(navigator.userAgent)) return false
  try {
    return document.createElement('canvas').getContext('2d') !== null
  } catch {
    return false
  }
}

class SceneBoundary extends Component<{ children: ReactNode; onError: () => void }, { failed: boolean }> {
  state = { failed: false }
  static getDerivedStateFromError() { return { failed: true } }
  componentDidCatch() { this.props.onError() }
  render() { return this.state.failed ? null : this.props.children }
}

export type WorldViewportProps = {
  living?: LivingWorld | null
  /** M15 road overlay; null for worlds without roads. */
  roads?: RoadOverlay | null
  map: Map
  citizens: Citizen[]
  structures: Structure[]
  settlement: Settlement | null
  settlementSites?: SettlementSite[]
  worldSeed: string | null
  operationalSpeed: number | null
  worldMinute?: number
  paused: boolean
  controlsEnabled?: boolean
  selectedCitizenId: string | null
  onSelectCitizen: (citizenId: string) => void
  onOpenSelected?: (citizenId: string) => void
  onViewFamily?: (citizenId: string) => void
  /** Only explicit family follow actions issue this request. */
  followRequest?: { citizenId: string; sequence: number; follow?: boolean } | null
  locateRequest?: { citizenId: string; sequence: number } | null
  onFollowingChange?: (id: string | null) => void
  onClearSelection?: () => void
  favorites?: ReadonlySet<string>
  favoritesNotice?: string | null
  onFavorite?: (id: string) => void
  /** Development art review only; ignored by production builds. */
  previewSeason?: Season
}

export function WorldViewport(props: WorldViewportProps) {
  const [overview, setOverview] = useState(() => !supportsCanvas())
  const [wholeMap, setWholeMap] = useState(false)
  const [resetToken, setResetToken] = useState(0)
  const [followCitizenId, setFollowCitizenId] = useState<string | null>(null)
  const [selectedSettlementId, setSelectedSettlementId] = useState<string | null>(null)
  const [focusedSettlementId, setFocusedSettlementId] = useState<string | null>(null)
  const [cameraLocateRequest, setCameraLocateRequest] = useState<{ citizenId: string; sequence: number; point: { x: number; y: number } } | null>(null)
  const [locatePoint, setLocatePoint] = useState<{ x: number; y: number } | null>(null)
  const [stats, setStats] = useState<IsoStats | null>(null)
  const [crowdIds, setCrowdIds] = useState<string[]>([])
  const [nudge, setNudge] = useState<CameraNudge>({ x: 0, z: 0, zoom: 0, sequence: 0 })
  const { followRequest, onFollowingChange, citizens } = props
  const changeFollow = (id: string | null) => {
    if (!id && followCitizenId) {
      const person = citizens.find(c => c.citizenId === followCitizenId && isCitizenPresent(c))
      if (person) setLocatePoint({ ...person.location })
    } else if (id) setLocatePoint(null)
    setFollowCitizenId(id); onFollowingChange?.(id)
  }
  useEffect(() => {
    if (!followRequest) return
    const { citizenId: id, follow = true } = followRequest
    const present = citizens.some(c => c.citizenId === id && isCitizenPresent(c))
    queueMicrotask(() => { setCrowdIds([]); setWholeMap(false); setCameraLocateRequest(null); setLocatePoint(!follow && present ? { ...citizens.find(c => c.citizenId === id)!.location } : null); setFollowCitizenId(follow && present ? id : null); onFollowingChange?.(follow && present ? id : null) })
    // Process each command once; live positions do not re-enable cancelled follow.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [followRequest, onFollowingChange])
  useEffect(() => {
    const request = props.locateRequest
    if (!request) return
    const person = props.citizens.find(c => c.citizenId === request.citizenId && isCitizenPresent(c))
    if (!person) return
    queueMicrotask(() => { setCrowdIds([]); setWholeMap(false); setLocatePoint({ ...person.location }); setCameraLocateRequest(previous => ({ citizenId: person.citizenId, sequence: (previous?.sequence ?? 0) + 1, point: { ...person.location } })); setFollowCitizenId(null); props.onFollowingChange?.(null) })
    // A locate command captures a position once. Live frames must not turn it into following.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [props.locateRequest])
  useEffect(() => {
    if (followCitizenId && !citizens.some(c => c.citizenId === followCitizenId && isCitizenPresent(c))) {
      queueMicrotask(() => { setFollowCitizenId(null); onFollowingChange?.(null) })
    }
  }, [followCitizenId, citizens, onFollowingChange])
  const reducedMotion = typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true
  const selected = props.citizens.find(citizen => citizen.citizenId === props.selectedCitizenId) ?? null
  const settlementSites = props.settlementSites ?? []
  const selectedSite = settlementSites.find(site => site.settlementId === selectedSettlementId) ?? settlementSites[0] ?? null
  const selectedActivity = selected ? citizenActivity(selected, props.living, props.structures).label : ''
  const crowdMembers = crowdIds.map(id => props.citizens.find(citizen => citizen.citizenId === id)).filter((citizen): citizen is Citizen => !!citizen && isCitizenPresent(citizen))
  const selectCitizen = (id: string) => { setCrowdIds([]); props.onSelectCitizen(id) }
  const effectiveFollowCitizenId = followCitizenId !== null && props.citizens.some(citizen => citizen.citizenId === followCitizenId && isCitizenPresent(citizen)) ? followCitizenId : null
  const following = selected !== null && effectiveFollowCitizenId === selected.citizenId
  const toggleFollow = () => { setWholeMap(false); setCameraLocateRequest(null); changeFollow(following ? null : selected?.citizenId ?? null) }
  const issueNudge = (x: number, z: number, zoom = 0) => { changeFollow(null); setCameraLocateRequest(null); setNudge(previous => ({ x, z, zoom, sequence: previous.sequence + 1 })) }
  const focusSettlementSite = (settlementId: string) => {
    setSelectedSettlementId(settlementId)
    setFocusedSettlementId(settlementId)
    setResetToken(value => value + 1)
    changeFollow(null)
    setLocatePoint(null)
    setCameraLocateRequest(null)
    setWholeMap(false)
  }
  const onKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') setCrowdIds([])
    else if (event.target !== event.currentTarget) return
    else if (event.key === 'ArrowLeft') issueNudge(-2, 0)
    else if (event.key === 'ArrowRight') issueNudge(2, 0)
    else if (event.key === 'ArrowUp') issueNudge(0, -2)
    else if (event.key === 'ArrowDown') issueNudge(0, 2)
    else if (event.key === '+' || event.key === '=') issueNudge(0, 0, 2)
    else if (event.key === '-') issueNudge(0, 0, -2)
    else return
    event.preventDefault()
  }

  return <section className="world-viewport" aria-labelledby="world-heading" tabIndex={0} onKeyDown={onKeyDown}>
    <div className="world-overlay-header"><div className="world-hud-top">
      <SettlementBadge worldMinute={props.worldMinute ?? 0} living={props.living} previewSeason={import.meta.env.DEV ? props.previewSeason : undefined} hint={overview && props.living ? 'Gold fields are ready to harvest · outlined sites have assigned work' : 'Drag to pan · scroll or pinch to zoom · tap a villager or crowd count'} />
      <div className="world-hud-right">
        <ResourceMeters settlement={props.settlement} />
    {settlementSites.length > 1 && <div className="world-site-control" aria-label="Settlement site controls">
      <label>Settlement site<select aria-label="Settlement site" value={selectedSite?.settlementId ?? ''} onChange={event => focusSettlementSite(event.target.value)}>
        {settlementSites.map(site => <option key={site.settlementId} value={site.settlementId}>Settlement {site.settlementId} · ({site.site.x}, {site.site.y})</option>)}
      </select></label>
      {selectedSite && <output aria-live="polite">{focusedSettlementId ? 'Focused · ' : 'Selected · '}Population {selectedSite.livingPopulation.toLocaleString()} · Food {selectedSite.foodStored.toLocaleString()} · Wood {selectedSite.woodStored.toLocaleString()} · Stone {selectedSite.stoneStored.toLocaleString()}</output>}
    </div>}
      </div>
    </div>
    {!overview && selected && <p className="world-activity-legend">Selected: → travel · W work · ! blocked · Z rest{props.paused ? ' · Paused' : reducedMotion ? ' · Reduced motion' : ''}</p>}
    <div className="world-toolbar" aria-label="World camera controls">
      <button type="button" className="hud-button hud-button-cream" onClick={() => { setResetToken(value => value + 1); changeFollow(null); setLocatePoint(null); setFocusedSettlementId(null); setCameraLocateRequest(null); setWholeMap(false) }}><HudIcon name="target" />Reset view</button>
      <button type="button" className="hud-button hud-button-cream" disabled={selected === null || !isCitizenPresent(selected)} onClick={toggleFollow}><HudIcon name="eye" />{following ? 'Unfollow' : 'Follow selected'}</button>
      <button type="button" className="hud-button hud-button-blue" onClick={() => setOverview(value => !value)}><HudIcon name={overview ? 'home' : 'map'} />{overview ? 'Village view' : 'Map overview'}</button>
      {overview && props.living && <button type="button" className="hud-button hud-button-blue" onClick={() => { changeFollow(null); setLocatePoint(null); setCameraLocateRequest(null); setWholeMap(value => !value) }}><HudIcon name="expand" />{wholeMap ? 'Settlement view' : 'Whole map'}</button>}
    </div>
    </div>
    <div className="world-stage">
      {overview ? <LegacyMap living={props.living} roads={props.roads} focusSettlement={!!props.living && !wholeMap} settlementSites={settlementSites} selectedSettlementId={selectedSite?.settlementId ?? null} focusedSettlementId={wholeMap ? null : focusedSettlementId} map={props.map} citizens={props.citizens} structures={props.structures} selectedCitizenId={props.selectedCitizenId} followCitizenId={effectiveFollowCitizenId} locatePoint={locatePoint} onSelectCitizen={selectCitizen} onPickCitizens={setCrowdIds} /> : <SceneBoundary onError={() => setOverview(true)}>
        <IsoWorld map={props.map} living={props.living} roads={props.roads} citizens={props.citizens} structures={props.structures} settlement={props.settlement} settlementSites={settlementSites} focusedSettlementId={focusedSettlementId} onFocusSettlementSite={focusSettlementSite} worldSeed={props.worldSeed} worldMinute={props.worldMinute ?? 0} operationalSpeed={props.operationalSpeed} paused={props.paused} reducedMotion={reducedMotion} controlsEnabled={props.controlsEnabled !== false} selectedCitizenId={props.selectedCitizenId} onSelectCitizen={selectCitizen} onPickCitizens={setCrowdIds} resetToken={resetToken} nudge={nudge} followCitizenId={effectiveFollowCitizenId} locateRequest={cameraLocateRequest} onManualCamera={() => { changeFollow(null); setCameraLocateRequest(null) }} onStats={diagnosticsEnabled ? setStats : undefined} previewSeason={import.meta.env.DEV ? props.previewSeason : undefined} />
      </SceneBoundary>}
    </div>
    {crowdMembers.length > 0 ? <section className="world-crowd-picker" aria-label="People at this spot">
      <div><strong>Choose a person · {crowdMembers.length}</strong><button type="button" onClick={() => setCrowdIds([])} aria-label="Close people picker">×</button></div>
      <ul>{crowdMembers.map(citizen => {
        const activity = citizenActivity(citizen, props.living, props.structures).label
        return <li key={citizen.citizenId}><button type="button" aria-label={`${citizen.name} · ${activity}`} onClick={() => selectCitizen(citizen.citizenId)}><strong>{citizen.name}</strong><span>{activity}</span></button></li>
      })}</ul>
    </section> : selected && <VillagerCard key={selected.citizenId} citizen={selected} activity={selectedActivity} portrait={`/assets/sprites/${villagerSprite(props.worldSeed, selected)}.svg`} following={following} onFollow={toggleFollow} onLocate={() => { changeFollow(null); setWholeMap(false); setLocatePoint({ ...selected.location }); setCameraLocateRequest(previous => ({ citizenId: selected.citizenId, sequence: (previous?.sequence ?? 0) + 1, point: { ...selected.location } })) }} favorite={props.favorites?.has(selected.citizenId)} notice={props.favoritesNotice} onFavorite={props.onFavorite ? () => props.onFavorite?.(selected.citizenId) : undefined} onClear={props.onClearSelection ? () => { changeFollow(null); setCameraLocateRequest(null); props.onClearSelection?.() } : undefined} onOpen={props.onOpenSelected ? () => props.onOpenSelected?.(selected.citizenId) : undefined} onViewFamily={props.onViewFamily ? () => props.onViewFamily?.(selected.citizenId) : undefined} />}

    {diagnosticsEnabled && !overview && stats && <output className="world-perf" aria-label="World view performance">{stats.fps} FPS · p95 {stats.p95.toFixed(1)} ms · {stats.sprites} sprites · {stats.chunks} ground chunks · {props.citizens.filter(isCitizenPresent).length} villagers · {stats.season} · shelter tier {stats.tier}</output>}
    <span className="world-accessibility-note">Starting site</span><span className="world-accessibility-note">Keyboard: arrow keys pan, +/− zoom. All citizen details remain available in Observer records.</span>
  </section>
}
