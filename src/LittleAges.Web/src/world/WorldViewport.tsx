import { isCitizenPresent, newcomerActivity } from '../newcomers'
import { Component, useState, type ReactNode } from 'react'
import type { Citizen, Map, RoadOverlay, Settlement, SettlementSite, Structure } from '../api'
import type { LivingWorld } from '../living'
import { festivalVisitorActivity, livingLabel, livingWorkStage } from '../living'
import { HudIcon, ResourceMeters, SettlementBadge, VillagerCard } from './Hud'
import { IsoWorld, type CameraNudge, type IsoStats } from './IsoWorld'
import { villagerSprite } from './iso/scene'
import type { Season } from './iso/seasons'
import { LegacyMap } from './LegacyMap'
import { restingHome } from './presentation'

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
  const [stats, setStats] = useState<IsoStats | null>(null)
  const [nudge, setNudge] = useState<CameraNudge>({ x: 0, z: 0, zoom: 0, sequence: 0 })
  const reducedMotion = typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true
  const selected = props.citizens.find(citizen => citizen.citizenId === props.selectedCitizenId) ?? null
  const settlementSites = props.settlementSites ?? []
  const selectedSite = settlementSites.find(site => site.settlementId === selectedSettlementId) ?? settlementSites[0] ?? null
  const selectedWork = props.living?.orders.find(order => order.citizenId === selected?.citizenId)
  const selectedActivity = selectedWork ? `${livingLabel(selectedWork.kind)} · ${livingWorkStage(selectedWork)}` : selected ? newcomerActivity(selected) ?? festivalVisitorActivity(props.living, selected) ?? (restingHome(selected, props.structures) ? 'Resting indoors' : `${livingLabel(selected.currentAction ?? '')} · ${livingLabel(selected.actionPhase ?? '')}`) : ''
  const effectiveFollowCitizenId = followCitizenId !== null && props.citizens.some(citizen => citizen.citizenId === followCitizenId && isCitizenPresent(citizen)) ? followCitizenId : null
  const following = selected !== null && effectiveFollowCitizenId === selected.citizenId
  const toggleFollow = () => setFollowCitizenId(current => current === selected?.citizenId ? null : selected?.citizenId ?? null)
  const issueNudge = (x: number, z: number, zoom = 0) => setNudge(previous => ({ x, z, zoom, sequence: previous.sequence + 1 }))
  const focusSettlementSite = (settlementId: string) => {
    setSelectedSettlementId(settlementId)
    setFocusedSettlementId(settlementId)
    setResetToken(value => value + 1)
    setFollowCitizenId(null)
    setWholeMap(false)
  }
  const focusSelectedSettlement = () => { if (selectedSite !== null) focusSettlementSite(selectedSite.settlementId) }
  const onKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'ArrowLeft') issueNudge(-2, 0)
    else if (event.key === 'ArrowRight') issueNudge(2, 0)
    else if (event.key === 'ArrowUp') issueNudge(0, -2)
    else if (event.key === 'ArrowDown') issueNudge(0, 2)
    else if (event.key === '+' || event.key === '=') issueNudge(0, 0, 2)
    else if (event.key === '-') issueNudge(0, 0, -2)
    else return
    event.preventDefault()
  }

  return <section className="world-viewport" aria-labelledby="world-heading" tabIndex={0} onKeyDown={onKeyDown}>
    <div className="world-hud-top">
      <SettlementBadge worldMinute={props.worldMinute ?? 0} living={props.living} previewSeason={import.meta.env.DEV ? props.previewSeason : undefined} hint={overview && props.living ? 'Gold fields are ready to harvest · outlined sites have active work' : 'Drag to pan · scroll or pinch to zoom · tap a villager to follow their day'} />
      <div className="world-hud-right">
        <ResourceMeters settlement={props.settlement} />
    {settlementSites.length > 1 && <div className="world-site-control" aria-label="Settlement site controls">
      <label>Settlement site<select aria-label="Settlement site" value={selectedSite?.settlementId ?? ''} onChange={event => setSelectedSettlementId(event.target.value)}>
        {settlementSites.map(site => <option key={site.settlementId} value={site.settlementId}>Settlement {site.settlementId} · ({site.site.x}, {site.site.y})</option>)}
      </select></label>
      {selectedSite && <output aria-live="polite">Population {selectedSite.livingPopulation.toLocaleString()} · Food {selectedSite.foodStored.toLocaleString()} · Wood {selectedSite.woodStored.toLocaleString()} · Stone {selectedSite.stoneStored.toLocaleString()}</output>}
      <button type="button" className="hud-button hud-button-teal" onClick={focusSelectedSettlement} disabled={selectedSite === null}>Focus site</button>
    </div>}
      </div>
    </div>
    <div className="world-toolbar" aria-label="World camera controls">
      <button type="button" className="hud-button hud-button-cream" onClick={() => { setResetToken(value => value + 1); setFollowCitizenId(null); setFocusedSettlementId(null) }}><HudIcon name="target" />Reset view</button>
      <button type="button" className="hud-button hud-button-cream" disabled={selected === null || !isCitizenPresent(selected)} onClick={toggleFollow}><HudIcon name="eye" />{following ? 'Unfollow' : 'Follow selected'}</button>
      <button type="button" className="hud-button hud-button-blue" onClick={() => setOverview(value => !value)}><HudIcon name={overview ? 'home' : 'map'} />{overview ? 'Village view' : 'Map overview'}</button>
      {overview && props.living && <button type="button" className="hud-button hud-button-blue" onClick={() => setWholeMap(value => !value)}><HudIcon name="expand" />{wholeMap ? 'Settlement view' : 'Whole map'}</button>}
    </div>
    <div className="world-stage">
      {overview ? <LegacyMap living={props.living} roads={props.roads} focusSettlement={!!props.living && !wholeMap} settlementSites={settlementSites} selectedSettlementId={selectedSite?.settlementId ?? null} focusedSettlementId={wholeMap ? null : focusedSettlementId} map={props.map} citizens={props.citizens} structures={props.structures} /> : <SceneBoundary onError={() => setOverview(true)}>
        <IsoWorld map={props.map} living={props.living} roads={props.roads} citizens={props.citizens} structures={props.structures} settlement={props.settlement} settlementSites={settlementSites} focusedSettlementId={focusedSettlementId} onFocusSettlementSite={focusSettlementSite} worldSeed={props.worldSeed} worldMinute={props.worldMinute ?? 0} operationalSpeed={props.operationalSpeed} paused={props.paused} reducedMotion={reducedMotion} controlsEnabled={props.controlsEnabled !== false} selectedCitizenId={props.selectedCitizenId} onSelectCitizen={props.onSelectCitizen} resetToken={resetToken} nudge={nudge} followCitizenId={effectiveFollowCitizenId} onStats={diagnosticsEnabled ? setStats : undefined} previewSeason={import.meta.env.DEV ? props.previewSeason : undefined} />
      </SceneBoundary>}
    </div>
    {selected && <VillagerCard citizen={selected} activity={selectedActivity} portrait={`/assets/sprites/${villagerSprite(props.worldSeed, selected)}.svg`} following={following} onFollow={toggleFollow} onOpen={props.onOpenSelected ? () => props.onOpenSelected?.(selected.citizenId) : undefined} />}
    {diagnosticsEnabled && !overview && stats && <output className="world-perf" aria-label="World view performance">{stats.fps} FPS · p95 {stats.p95.toFixed(1)} ms · {stats.sprites} sprites · {stats.chunks} ground chunks · {props.citizens.filter(isCitizenPresent).length} villagers · {stats.season} · shelter tier {stats.tier}</output>}
    <span className="world-accessibility-note">Starting site</span><span className="world-accessibility-note">Keyboard: arrow keys pan, +/− zoom. All citizen details remain available in Observer records.</span>
  </section>
}
