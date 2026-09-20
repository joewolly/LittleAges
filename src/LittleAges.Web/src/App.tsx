import { lazy, Suspense, useCallback, useEffect, useRef, useState } from 'react'
import {
  buildHistoryQuery,
  buildStatisticsQuery,
  fetchBiography,
  fetchCitizens,
  fetchHealth,
  fetchHistory,
  fetchHousehold,
  fetchHouseholds,
  fetchMap,
  fetchRelationships,
  fetchSettlement,
  fetchStatistics,
  fetchStatus,
  fetchStructures,
  changeSimulationSpeed,
  pauseSimulation,
  resumeSimulation,
  SAFE_OPERATIONAL_SPEEDS,
  type Citizen,
  type CitizenBiography,
  type Health,
  type HistoricalEvent,
  type HistoricalEventType,
  type Household,
  type Map,
  type Relationship,
  type Settlement,
  type StatisticsQuery,
  type StatisticsSample,
  type Status,
  type Structure,
} from './api'
import { createWorldConnection, type WorldConnection } from './live'
import { retainStructures } from './world/presentation'
import { LegacyMap } from './world/LegacyMap'
import { SceneLoadBoundary } from './world/SceneLoadBoundary'

const loadWorldViewport = () => import('./world/WorldViewport').then(module => ({ default: module.WorldViewport }))
const WorldViewport = lazy(loadWorldViewport)

const initialStatus: Status = { state: 'Unknown', worldMinute: null, pendingEventCount: null, worldSeed: null, error: null, paused: false, operationalSpeed: null }
const historyTypes: HistoricalEventType[] = ['WorldCreated', 'SettlementFounded', 'CitizenBorn', 'CitizenDied', 'PartnershipFormed', 'FriendshipFormed', 'RivalryFormed', 'HouseholdCreated', 'StructureStarted', 'StructureCompleted', 'PopulationMilestone', 'ResourceShortageStarted', 'ResourceShortageEnded', 'CitizenSpecializationChanged', 'SeasonStarted']
const HISTORY_PAGE_SIZE = 50
const STATISTICS_PAGE_SIZE = 100
const REST_VISIBLE_FALLBACK_INTERVAL_MS = 2_000
const REST_HIDDEN_FALLBACK_INTERVAL_MS = 10_000
const OBSERVER_TABS = ['Overview', 'Citizens', 'Buildings', 'History', 'Statistics'] as const
type ObserverTab = typeof OBSERVER_TABS[number]
const WORLD_SEASONS = ['Spring', 'Summer', 'Autumn', 'Winter'] as const

type LiveConnectionState = 'connecting' | 'connected' | 'reconnecting' | 'disconnected' | 'unavailable'

function formatMinute(minute: number | null | undefined) { return minute === null || minute === undefined ? '—' : new Intl.NumberFormat('en-US').format(minute) }
function formatValue(value: number | null | undefined) { return value === null || value === undefined ? '—' : new Intl.NumberFormat('en-US').format(value) }
function formatCalendarDate(worldMinute: number) {
  const minutesPerDay = 24 * 60
  const daysPerMonth = 30
  const monthsPerYear = 12
  const daysPerYear = daysPerMonth * monthsPerYear
  const dayCount = Math.floor(worldMinute / minutesPerDay)
  const dayOfYear = dayCount % daysPerYear
  const month = Math.floor(dayOfYear / daysPerMonth) + 1
  const day = (dayOfYear % daysPerMonth) + 1
  const minuteOfDay = worldMinute % minutesPerDay
  const hour = Math.floor(minuteOfDay / 60)
  const minute = minuteOfDay % 60
  const time = `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`
  return `Year ${Math.floor(dayCount / daysPerYear)} · ${WORLD_SEASONS[Math.floor((month - 1) / 3)]} · Month ${month}, Day ${day} · ${time}`
}
function percent(complete: number, required: number) { return required === 0 ? 100 : Math.min(100, Math.round((complete / required) * 100)) }
function structureLabel(structure: Structure) { return `${structure.type} at (${structure.location.x}, ${structure.location.y})` }
function errorText(reason: unknown, fallback: string) { return reason instanceof Error ? reason.message : fallback }

function FamilyDetails({ citizen }: { citizen: Citizen }) { return <section className="card-section" aria-label={`${citizen.name} family`}><h4>Family</h4><dl className="citizen-details"><div><dt>Parents</dt><dd>{[citizen.parentAId, citizen.parentBId].filter((id): id is string => id !== null).join(' · ') || '—'}</dd></div><div><dt>Partner</dt><dd>{citizen.partnerId ?? '—'}</dd></div><div><dt>Children</dt><dd>{citizen.childrenIds.join(' · ') || '—'}</dd></div><div><dt>Household</dt><dd>{citizen.householdId ?? '—'}</dd></div></dl></section> }
function DemographicsDetails({ citizen }: { citizen: Citizen }) { return <section className="card-section" aria-label={`${citizen.name} demographics`}><h4>Demographics</h4><dl className="citizen-details"><div><dt>Age</dt><dd>{formatValue(citizen.age)} years</dd></div><div><dt>Life stage</dt><dd>{citizen.lifeStage}</dd></div></dl></section> }

function RelationshipDetails({ citizen, relationships, loading, error }: { citizen: Citizen; relationships: Relationship[] | null; loading: boolean; error: string | null }) {
  return <section className="relationship-inspector" aria-labelledby="relationships-heading"><div className="section-heading"><span className="section-kicker">Citizen inspector</span><h2 id="relationships-heading">Relationships for {citizen.name}</h2></div>{loading && <p role="status">Loading relationships…</p>}{error && <p className="notice" role="alert">Relationships unavailable: {error}</p>}{relationships !== null && relationships.length === 0 && <p>No recorded relationships.</p>}{relationships !== null && relationships.length > 0 && <div className="relationship-grid">{relationships.map(relationship => <article className="relationship-card" key={relationship.otherCitizenId}><h3>{relationship.otherCitizenName}</h3><p>{relationship.label} · Citizen {relationship.otherCitizenId}</p><dl className="citizen-details"><div><dt>Affinity</dt><dd>{formatValue(relationship.affinity)}</dd></div><div><dt>Trust</dt><dd>{formatValue(relationship.trust)}</dd></div><div><dt>Conflict</dt><dd>{formatValue(relationship.conflict)}</dd></div><div><dt>Familiarity</dt><dd>{formatValue(relationship.familiarity)}</dd></div><div><dt>Interactions</dt><dd>{formatValue(relationship.interactionCount)}</dd></div><div><dt>Last interaction</dt><dd>{formatMinute(relationship.lastInteractionMinute)}</dd></div></dl></article>)}</div>}</section>
}

function HouseholdDetails({ household }: { household: Household }) { return <article className="household-detail"><h3>Household {household.householdId}</h3><dl className="citizen-details"><div><dt>Members</dt><dd>{household.memberIds.join(' · ') || '—'}</dd></div><div><dt>Living members</dt><dd>{household.livingMemberIds.join(' · ') || '—'}</dd></div><div><dt>Dwelling</dt><dd>{household.dwellingStructureId === null ? '—' : `Structure ${household.dwellingStructureId}`}</dd></div><div><dt>Partnership</dt><dd>{household.partnerPair?.join(' · ') ?? '—'}</dd></div><div><dt>Children</dt><dd>{household.childrenIds.join(' · ') || '—'}</dd></div><div><dt>Created</dt><dd>{formatMinute(household.createdMinute)}</dd></div><div><dt>Dissolved</dt><dd>{household.dissolvedMinute === null ? 'Active' : formatMinute(household.dissolvedMinute)}</dd></div></dl></article> }

function eventTypeLabel(type: string) { return type.replace(/([a-z])([A-Z])/g, '$1 $2') }
function HistoryEventCard({ event }: { event: HistoricalEvent }) { return <article className="history-event"><div className="history-event-meta"><span>{eventTypeLabel(event.eventType)}</span><span>{event.importance}</span><span>{formatCalendarDate(event.worldMinute)}</span><span>Minute {formatMinute(event.worldMinute)}</span></div><p>{event.summary}</p><div className="history-links">{event.citizenLinks.map(link => <span key={`${link.citizenId}-${link.role}`}>Citizen {link.citizenId} · {link.role}</span>)}{event.structureLinks.map(link => <span key={`${link.structureId}-${link.role}`}>Structure {link.structureId} · {link.role}</span>)}</div>{event.origin === 'MigrationBackfill' && <small>Recovered from exact M5 state at upgrade</small>}</article> }

function HistorySurface({ citizens, refreshToken }: { citizens: Citizen[]; refreshToken: number }) {
  const [events, setEvents] = useState<HistoricalEvent[]>([]); const [error, setError] = useState<string | null>(null); const [loading, setLoading] = useState(true); const [loadingMore, setLoadingMore] = useState(false); const [hasMore, setHasMore] = useState(false)
  const [minimumImportance, setMinimumImportance] = useState(2); const [eventType, setEventType] = useState<HistoricalEventType | ''>(''); const [citizenId, setCitizenId] = useState(''); const [familyCitizenId, setFamilyCitizenId] = useState(''); const [structureId, setStructureId] = useState(''); const [fromMinute, setFromMinute] = useState(''); const [toMinute, setToMinute] = useState(''); const [revision, setRevision] = useState(0); const requestRevision = useRef(0)
  const filter = { minimumImportance, eventType: eventType || undefined, citizenId: citizenId || undefined, familyCitizenId: familyCitizenId || undefined, structureId: structureId || undefined, fromMinute: fromMinute === '' ? undefined : Number(fromMinute), toMinute: toMinute === '' ? undefined : Number(toMinute), limit: 50 }
  const activeFilter = useRef(filter)
  useEffect(() => {
    const request = requestRevision.current + 1
    requestRevision.current = request
    queueMicrotask(() => { if (requestRevision.current === request) { setLoading(true); setLoadingMore(false); setError(null); setEvents([]); setHasMore(false) } })
    void fetchHistory(activeFilter.current).then(value => {
      if (requestRevision.current === request) { setEvents(value); setHasMore(value.length === HISTORY_PAGE_SIZE) }
    }).catch(reason => {
      if (requestRevision.current === request) { setError(errorText(reason, 'History could not be read.')); setHasMore(false) }
    }).finally(() => {
      if (requestRevision.current === request) setLoading(false)
    })
    return () => { if (requestRevision.current === request) requestRevision.current += 1 }
  }, [refreshToken, revision])
  const applyFilters = () => { try { buildHistoryQuery(filter); activeFilter.current = filter; setLoading(true); setError(null); setEvents([]); setHasMore(false); setLoadingMore(false); setRevision(value => value + 1) } catch (reason: unknown) { setError(errorText(reason, 'Invalid history filter.')) } }
  const loadMore = () => { const beforeEventId = events[events.length - 1]?.eventId; if (!beforeEventId || !hasMore || loadingMore) return; const request = requestRevision.current; setLoadingMore(true); void fetchHistory({ ...activeFilter.current, beforeEventId }).then(value => { if (requestRevision.current === request) { setEvents(previous => { const knownIds = new Set(previous.map(event => event.eventId)); return [...previous, ...value.filter(event => !knownIds.has(event.eventId))] }); setHasMore(value.length === HISTORY_PAGE_SIZE) } }).catch(reason => { if (requestRevision.current === request) setError(errorText(reason, 'More history could not be read.')) }).finally(() => { if (requestRevision.current === request) setLoadingMore(false) }) }
  return <section className="history-surface" aria-labelledby="history-heading"><div className="section-heading"><span className="section-kicker">Historical record</span><h2 id="history-heading">What the world remembers</h2><p className="section-description">Immutable factual events, newest first. The default view includes importance 2 and above.</p></div><form className="history-filters" onSubmit={event => { event.preventDefault(); applyFilters() }} aria-label="History filters"><label>Minimum importance<select value={minimumImportance} onChange={event => setMinimumImportance(Number(event.target.value))}><option value={0}>Debug</option><option value={1}>Routine</option><option value={2}>Personal</option><option value={3}>Notable</option><option value={4}>Major</option><option value={5}>Historic</option></select></label><label>Event type<select value={eventType} onChange={event => setEventType(event.target.value as HistoricalEventType | '')}><option value="">All event types</option>{historyTypes.map(type => <option key={type} value={type}>{eventTypeLabel(type)}</option>)}</select></label><label>Citizen ID<input inputMode="numeric" value={citizenId} onChange={event => setCitizenId(event.target.value)} placeholder="Any citizen" /></label><label>Family root ID<input inputMode="numeric" value={familyCitizenId} onChange={event => setFamilyCitizenId(event.target.value)} placeholder="Any family" /></label><label>Structure ID<input inputMode="numeric" value={structureId} onChange={event => setStructureId(event.target.value)} placeholder="Any structure" /></label><label>From minute<input inputMode="numeric" value={fromMinute} onChange={event => setFromMinute(event.target.value)} /></label><label>To minute<input inputMode="numeric" value={toMinute} onChange={event => setToMinute(event.target.value)} /></label><button type="submit" className="observer-button">Apply filters</button></form>{loading && <p role="status">Loading history…</p>}{error && <p className="notice" role="alert">History unavailable: {error}</p>}{!loading && !error && events.length === 0 && <p>No historical events match these filters.</p>}{events.length > 0 && <div className="history-list">{events.map(event => <HistoryEventCard key={event.eventId} event={event} />)}</div>}{hasMore && events.length > 0 && <button type="button" className="observer-button" onClick={loadMore} disabled={loadingMore}>{loadingMore ? 'Loading more…' : 'Load older events'}</button>}{citizens.length === 0 && <p className="section-note">Citizen and family filters become available when the roster is loaded.</p>}</section>
}

function BiographySurface({ biography, loading, error }: { biography: CitizenBiography | null; loading: boolean; error: string | null }) { if (biography === null && !loading && error === null) return <p className="section-note">Select a citizen to inspect their factual biography.</p>; return <section className="biography-surface" aria-labelledby="biography-heading"><div className="section-heading"><span className="section-kicker">Citizen inspector</span><h2 id="biography-heading">Biography</h2></div>{loading && <p role="status">Loading biography…</p>}{error && <p className="notice" role="alert">Biography unavailable: {error}</p>}{biography !== null && <><div className="biography-summary"><h3>{biography.citizen.name}</h3><p>{biography.citizen.lifeStage} · {biography.citizen.occupation} · {biography.citizen.isAlive ? 'Alive' : 'Dead'}</p><dl className="citizen-details"><div><dt>Birth</dt><dd>{formatMinute(biography.birthMinute)}</dd></div><div><dt>Death</dt><dd>{biography.deathMinute === null ? '—' : `${formatMinute(biography.deathMinute)} · ${biography.deathCause ?? 'Unknown cause'}`}</dd></div><div><dt>Parents</dt><dd>{biography.parentIds.join(' · ') || '—'}</dd></div><div><dt>Partner</dt><dd>{biography.partnerId ?? '—'}</dd></div><div><dt>Children</dt><dd>{biography.childrenIds.join(' · ') || '—'}</dd></div><div><dt>Household</dt><dd>{biography.citizen.householdId ?? '—'}</dd></div></dl></div><div className="biography-columns"><div><h3>Notable timeline</h3>{biography.events.length === 0 ? <p>No notable events recorded.</p> : <ol className="biography-timeline">{biography.events.map(event => <li key={event.eventId}><span>Minute {formatMinute(event.worldMinute)}</span><strong>{event.summary}</strong></li>)}</ol>}</div><div><h3>Important memories</h3>{biography.memories.length === 0 ? <p>No structured memories recorded.</p> : <ul className="memory-list">{biography.memories.map(memory => <li key={`${memory.eventId}-${memory.memoryType}`}><strong>{eventTypeLabel(memory.memoryType)}</strong><span>Minute {formatMinute(memory.createdMinute)} · valence {memory.emotionalValence > 0 ? '+' : ''}{memory.emotionalValence}</span></li>)}</ul>}</div></div></>}</section> }

function StatisticsSurface({ samples, loading, error, hasMore, refreshToken, onQuery, onRefresh }: { samples: StatisticsSample[] | null; loading: boolean; error: string | null; hasMore: boolean; refreshToken: number; onQuery: (query: StatisticsQuery, append: boolean) => void; onRefresh: () => void }) {
  const [fromMinute, setFromMinute] = useState('')
  const [toMinute, setToMinute] = useState('')
  const [rangeError, setRangeError] = useState<string | null>(null)
  useEffect(() => {
    onRefresh()
  }, [onRefresh, refreshToken])
  const maxPopulation = samples === null ? 1 : Math.max(...samples.map(sample => sample.population), 1)
  const readRange = (): StatisticsQuery => ({ fromMinute: fromMinute === '' ? undefined : Number(fromMinute), toMinute: toMinute === '' ? undefined : Number(toMinute), limit: STATISTICS_PAGE_SIZE })
  const applyRange = () => { try { const query = readRange(); buildStatisticsQuery(query); setRangeError(null); onQuery(query, false) } catch (reason: unknown) { setRangeError(errorText(reason, 'Invalid statistics range.')) } }
  const loadNext = () => {
    if (!hasMore || loading || samples === null || samples.length === 0) return
    const lastMinute = samples[samples.length - 1].worldMinute
    if (lastMinute >= Number.MAX_SAFE_INTEGER) { setRangeError('No later statistics samples can be requested safely.'); return }
    try { const query: StatisticsQuery = { fromMinute: lastMinute + 1, toMinute: toMinute === '' ? undefined : Number(toMinute), limit: STATISTICS_PAGE_SIZE }; buildStatisticsQuery(query); setRangeError(null); onQuery(query, true) } catch (reason: unknown) { setRangeError(errorText(reason, 'More statistics could not be requested.')) }
  }
  return <section className="statistics-surface" aria-labelledby="statistics-heading"><div className="section-heading"><span className="section-kicker">Historical statistics</span><h2 id="statistics-heading">Monthly measures</h2><p className="section-description">Append-only samples at each 43,200-minute boundary.</p></div><form className="statistics-filters" onSubmit={event => { event.preventDefault(); applyRange() }} aria-label="Statistics range"><label>Statistics from minute<input inputMode="numeric" value={fromMinute} onChange={event => setFromMinute(event.target.value)} /></label><label>Statistics to minute<input inputMode="numeric" value={toMinute} onChange={event => setToMinute(event.target.value)} /></label><button type="submit" className="observer-button">Apply range</button></form>{rangeError && <p className="notice" role="alert">{rangeError}</p>}{loading && <p role="status">Loading statistics…</p>}{error && <p className="notice" role="alert">Statistics unavailable: {error}</p>}{samples !== null && samples.length === 0 && <p>No monthly samples are available yet.</p>}{samples !== null && samples.length > 0 && <><div className="population-trend" aria-label="Population trend">{samples.map(sample => <span key={sample.worldMinute} style={{ height: `${Math.max(8, Math.round((sample.population / maxPopulation) * 100))}%` }} title={`Minute ${sample.worldMinute}: population ${sample.population}`} />)}</div><div className="table-scroll"><table><caption>Historical samples</caption><thead><tr><th>Minute</th><th>Population</th><th>Births</th><th>Deaths</th><th>Food stored</th><th>Produced</th><th>Consumed</th><th>Wood</th><th>Stone</th><th>Shelter</th><th>Avg health</th><th>Avg hunger</th></tr></thead><tbody>{samples.map(sample => <tr key={sample.worldMinute}><td>{formatMinute(sample.worldMinute)}</td><td>{formatValue(sample.population)}</td><td>{formatValue(sample.birthsPeriod)}</td><td>{formatValue(sample.deathsPeriod)}</td><td>{formatValue(sample.foodStored)}</td><td>{formatValue(sample.foodProducedPeriod)}</td><td>{formatValue(sample.foodConsumedPeriod)}</td><td>{formatValue(sample.woodStored)}</td><td>{formatValue(sample.stoneStored)}</td><td>{formatValue(sample.shelterCapacity)}</td><td>{formatValue(sample.averageHealth)}</td><td>{formatValue(sample.averageHunger)}</td></tr>)}</tbody></table></div>{hasMore && <button type="button" className="observer-button" onClick={loadNext} disabled={loading}>{loading ? 'Loading more…' : 'Load later samples'}</button>}</>}</section>
}

export function App() {
  const [health, setHealth] = useState<Health | null>(null); const [status, setStatus] = useState(initialStatus); const [error, setError] = useState<string | null>(null); const [mapError, setMapError] = useState<string | null>(null); const [citizens, setCitizens] = useState<Citizen[] | null>(null); const [settlement, setSettlement] = useState<Settlement | null>(null); const [structures, setStructures] = useState<Structure[] | null>(null); const [map, setMap] = useState<Map | null>(null); const [households, setHouseholds] = useState<Household[] | null>(null)
  const [controlError, setControlError] = useState<string | null>(null); const [controlBusy, setControlBusy] = useState(false)
  const [recordsOpen, setRecordsOpen] = useState(false); const [recordsTab, setRecordsTab] = useState<ObserverTab>('Overview'); const recordsButton = useRef<HTMLButtonElement>(null); const recordsCloseButton = useRef<HTMLButtonElement>(null)
  const [selectedCitizenId, setSelectedCitizenId] = useState<string | null>(null); const [relationshipCitizenId, setRelationshipCitizenId] = useState<string | null>(null); const [selectedBiographyCitizenId, setSelectedBiographyCitizenId] = useState<string | null>(null); const [relationships, setRelationships] = useState<Relationship[] | null>(null); const [relationshipError, setRelationshipError] = useState<string | null>(null); const [relationshipsLoading, setRelationshipsLoading] = useState(false); const [selectedHouseholdId, setSelectedHouseholdId] = useState<string | null>(null); const [selectedHousehold, setSelectedHousehold] = useState<Household | null>(null); const [householdError, setHouseholdError] = useState<string | null>(null); const [biography, setBiography] = useState<CitizenBiography | null>(null); const [biographyError, setBiographyError] = useState<string | null>(null); const [biographyLoading, setBiographyLoading] = useState(false); const [statistics, setStatistics] = useState<StatisticsSample[] | null>(null); const [statisticsError, setStatisticsError] = useState<string | null>(null); const [statisticsLoading, setStatisticsLoading] = useState(true); const [statisticsHasMore, setStatisticsHasMore] = useState(false); const [liveConnectionState, setLiveConnectionState] = useState<LiveConnectionState>('connecting'); const [liveRefreshRevision, setLiveRefreshRevision] = useState(0)
  useEffect(() => {
    if (citizens && citizens.length > 0 && !citizens.some(citizen => citizen.citizenId === selectedCitizenId)) queueMicrotask(() => setSelectedCitizenId(citizens[0].citizenId))
  }, [citizens, selectedCitizenId])
  const relationshipRequest = useRef(0); const householdRequest = useRef(0); const biographyRequest = useRef(0); const statisticsRequest = useRef(0); const statisticsBaseQuery = useRef<StatisticsQuery>({ limit: STATISTICS_PAGE_SIZE })
  useEffect(() => {
    let active = true
    let fallbackTimer: number | null = null
    let detailsTimer: number | null = null
    let connection: WorldConnection | null = null
    let signalRConnected = false
    let reconnecting = false
    let connectionStarting = false
    let refreshInFlight: Promise<void> | null = null
    let refreshPending = false
    let mapLoaded = false
    let requestRevision = 0
    let streamedRevision = -1
    let detailsInFlight: Promise<void> | null = null

    const clearFallback = () => {
      if (fallbackTimer !== null) { window.clearTimeout(fallbackTimer); fallbackTimer = null }
    }
    const clearDetailsTimer = () => {
      if (detailsTimer !== null) { window.clearTimeout(detailsTimer); detailsTimer = null }
    }
    const scheduleFallback = () => {
      if (!active || (signalRConnected && mapLoaded) || fallbackTimer !== null) return
      const delay = document.visibilityState === 'hidden' ? REST_HIDDEN_FALLBACK_INTERVAL_MS : REST_VISIBLE_FALLBACK_INTERVAL_MS
      fallbackTimer = window.setTimeout(() => { fallbackTimer = null; void requestDetailsRefresh(false, false); void requestRefresh(); if (!reconnecting) void connect() }, delay)
    }
    const loadAuthoritative = async (includeMap: boolean): Promise<boolean> => {
      const request = requestRevision + 1
      requestRevision = request
      const startingStreamRevision = streamedRevision
      const mapPromise = includeMap ? fetchMap().then(value => ({ ok: true as const, value })).catch(reason => ({ ok: false as const, reason })) : null
      const corePromise = Promise.allSettled([fetchHealth(), fetchStatus(), fetchCitizens(), fetchStructures()])
      const [mapResult, coreResults] = await Promise.all([mapPromise, corePromise])
      if (!active || requestRevision !== request) return false
      if (mapResult !== null) {
        if (mapResult.ok) { mapLoaded = true; setMap(mapResult.value); setMapError(null) }
        else {
          setMapError(errorText(mapResult.reason, 'The map could not be read.'))
        }
      }
      const [healthResult, statusResult, citizensResult, structuresResult] = coreResults
      try {
        if (healthResult.status === 'rejected') throw healthResult.reason
        if (statusResult.status === 'rejected') throw statusResult.reason
        if (citizensResult.status === 'rejected') throw citizensResult.reason
        if (structuresResult.status === 'rejected') throw structuresResult.reason
        if (streamedRevision === startingStreamRevision) {
          setHealth(healthResult.value); setStatus(statusResult.value); setCitizens(citizensResult.value); setStructures(previous => retainStructures(previous, structuresResult.value)); setError(null)
        }
        return true
      } catch (reason: unknown) {
        setError(errorText(reason, 'The server could not be reached.'))
        return false
      }
    }
    const requestDetailsRefresh = (notify = true, includeHealth = true): Promise<void> => {
      if (!active) return Promise.resolve()
      if (detailsInFlight !== null) return detailsInFlight
      const run = Promise.allSettled([includeHealth ? fetchHealth() : Promise.resolve(null), fetchSettlement(), fetchHouseholds()]).then(results => {
        if (!active) return
        const [healthResult, settlementResult, householdsResult] = results
        if (healthResult.status === 'rejected' || settlementResult.status === 'rejected' || householdsResult.status === 'rejected') return
        if (healthResult.value !== null) setHealth(healthResult.value)
        setSettlement(settlementResult.value); setHouseholds(householdsResult.value)
        if (notify) setLiveRefreshRevision(value => value + 1)
      }).finally(() => { if (detailsInFlight === run) detailsInFlight = null })
      detailsInFlight = run
      return run
    }
    const scheduleDetailsRefresh = () => {
      if (!active || detailsTimer !== null) return
      detailsTimer = window.setTimeout(() => { detailsTimer = null; void requestDetailsRefresh() }, REST_VISIBLE_FALLBACK_INTERVAL_MS)
    }
    const requestRefresh = (): Promise<void> => {
      if (!active) return Promise.resolve()
      refreshPending = true
      if (refreshInFlight !== null) return refreshInFlight
      const run = (async () => {
        while (active && refreshPending) {
          refreshPending = false
          const includeMap = !mapLoaded
          if (await loadAuthoritative(includeMap) && active && !includeMap) setLiveRefreshRevision(value => value + 1)
        }
      })()
      refreshInFlight = run
      void run.then(() => {
        if (refreshInFlight === run) refreshInFlight = null
        if (active && refreshPending) void requestRefresh()
        else scheduleFallback()
      }, () => {
        if (refreshInFlight === run) refreshInFlight = null
        if (active && refreshPending) void requestRefresh()
      })
      return run
    }
    const connect = async () => {
      if (!active || signalRConnected || reconnecting || connectionStarting) return
      connectionStarting = true
      try {
        if (connection === null) {
          connection = createWorldConnection()
          connection.onWorldChanged(() => { scheduleDetailsRefresh() })
          connection.onWorldFrame(frame => {
            if (!active || frame.revision <= streamedRevision) return
            streamedRevision = frame.revision
            signalRConnected = true; if (mapLoaded) clearFallback(); else scheduleFallback(); setLiveConnectionState('connected')
            setStatus(frame.status); setCitizens(frame.citizens); setStructures(previous => retainStructures(previous, frame.structures)); setError(null)
            scheduleDetailsRefresh()
          })
          connection.onStreamError(() => { if (active) { signalRConnected = false; setLiveConnectionState('unavailable'); void requestRefresh(); scheduleFallback() } })
          connection.onReconnecting(() => { if (active) { signalRConnected = false; reconnecting = true; setLiveConnectionState('reconnecting'); scheduleFallback() } })
          connection.onReconnected(() => { if (active) { streamedRevision = -1; requestRevision += 1; signalRConnected = true; reconnecting = false; setLiveConnectionState('connected'); if (mapLoaded) clearFallback(); else scheduleFallback(); void requestDetailsRefresh() } })
          connection.onClose(() => { if (active) { signalRConnected = false; reconnecting = false; setLiveConnectionState('disconnected'); scheduleFallback() } })
        }
        streamedRevision = -1
        await connection.start()
        if (active) {
          signalRConnected = true
          setLiveConnectionState('connected')
          if (mapLoaded) clearFallback()
          else scheduleFallback()
          await requestDetailsRefresh()
        }
      } catch {
        if (active) { signalRConnected = false; setLiveConnectionState('unavailable'); scheduleFallback() }
      } finally {
        connectionStarting = false
      }
    }
    void (async () => {
      // React's boundary handles a failed lazy import when rendered. Preloading
      // must not create a separate unhandled rejection.
      void loadWorldViewport().catch(() => undefined)
      void requestDetailsRefresh(false, false)
      await requestRefresh()
      if (active) await connect()
    })()
    const onVisibilityChange = () => { if (!signalRConnected) { clearFallback(); scheduleFallback() } }
    document.addEventListener('visibilitychange', onVisibilityChange)
    return () => {
      active = false; refreshPending = false; clearFallback(); clearDetailsTimer(); requestRevision += 1
      document.removeEventListener('visibilitychange', onVisibilityChange)
      if (connection !== null) void connection.stop().catch(() => undefined)
    }
  }, [])
  const requestStatistics = useCallback((query: StatisticsQuery, append: boolean) => { if (!append) statisticsBaseQuery.current = query; const request = statisticsRequest.current + 1; statisticsRequest.current = request; setStatisticsLoading(true); setStatisticsError(null); void fetchStatistics(query).then(value => { if (statisticsRequest.current !== request) return; setStatistics(previous => { if (!append || previous === null) return value; const byMinute = new Map(previous.map(sample => [sample.worldMinute, sample])); for (const sample of value) byMinute.set(sample.worldMinute, sample); return [...byMinute.values()].sort((first, second) => first.worldMinute - second.worldMinute) }); setStatisticsHasMore(value.length === STATISTICS_PAGE_SIZE) }).catch(reason => { if (statisticsRequest.current === request) setStatisticsError(errorText(reason, 'Statistics could not be read.')) }).finally(() => { if (statisticsRequest.current === request) setStatisticsLoading(false) }) }, [])
  const refreshStatistics = useCallback(() => { requestStatistics(statisticsBaseQuery.current, false) }, [requestStatistics])
  useEffect(() => {
    const request = relationshipRequest.current + 1
    relationshipRequest.current = request
    if (relationshipCitizenId === null) { queueMicrotask(() => { if (relationshipRequest.current === request) { setRelationships(null); setRelationshipsLoading(false) } }); return () => { if (relationshipRequest.current === request) relationshipRequest.current += 1 } }
    queueMicrotask(() => { if (relationshipRequest.current === request) { setRelationshipsLoading(true); setRelationshipError(null); setRelationships(null) } })
    void fetchRelationships(relationshipCitizenId).then(value => { if (relationshipRequest.current === request) setRelationships(value) }).catch(reason => { if (relationshipRequest.current === request) setRelationshipError(errorText(reason, 'The relationships could not be read.')) }).finally(() => { if (relationshipRequest.current === request) setRelationshipsLoading(false) })
    return () => { if (relationshipRequest.current === request) relationshipRequest.current += 1 }
  }, [liveRefreshRevision, relationshipCitizenId])
  useEffect(() => {
    const request = biographyRequest.current + 1
    biographyRequest.current = request
    if (selectedBiographyCitizenId === null) { queueMicrotask(() => { if (biographyRequest.current === request) { setBiography(null); setBiographyLoading(false) } }); return () => { if (biographyRequest.current === request) biographyRequest.current += 1 } }
    queueMicrotask(() => { if (biographyRequest.current === request) { setBiographyLoading(true); setBiographyError(null); setBiography(null) } })
    void fetchBiography(selectedBiographyCitizenId).then(value => { if (biographyRequest.current === request) setBiography(value) }).catch(reason => { if (biographyRequest.current === request) setBiographyError(errorText(reason, 'The biography could not be read.')) }).finally(() => { if (biographyRequest.current === request) setBiographyLoading(false) })
    return () => { if (biographyRequest.current === request) biographyRequest.current += 1 }
  }, [liveRefreshRevision, selectedBiographyCitizenId])
  useEffect(() => {
    const request = householdRequest.current + 1
    householdRequest.current = request
    if (selectedHouseholdId === null) { queueMicrotask(() => { if (householdRequest.current === request) setSelectedHousehold(null) }); return () => { if (householdRequest.current === request) householdRequest.current += 1 } }
    queueMicrotask(() => { if (householdRequest.current === request) { setSelectedHousehold(null); setHouseholdError(null) } })
    void fetchHousehold(selectedHouseholdId).then(value => { if (householdRequest.current === request) setSelectedHousehold(value) }).catch(reason => { if (householdRequest.current === request) setHouseholdError(errorText(reason, 'The household could not be read.')) })
    return () => { if (householdRequest.current === request) householdRequest.current += 1 }
  }, [liveRefreshRevision, selectedHouseholdId])
  useEffect(() => () => { relationshipRequest.current += 1; householdRequest.current += 1; biographyRequest.current += 1; statisticsRequest.current += 1 }, [])
  useEffect(() => {
    if (!recordsOpen) return
    queueMicrotask(() => recordsCloseButton.current?.focus())
    const closeOnEscape = (event: KeyboardEvent) => { if (event.key === 'Escape') { setRecordsOpen(false); queueMicrotask(() => recordsButton.current?.focus()) } }
    window.addEventListener('keydown', closeOnEscape)
    return () => window.removeEventListener('keydown', closeOnEscape)
  }, [recordsOpen])
  const selectCitizen = (citizenId: string) => { setSelectedCitizenId(citizenId); setRelationshipCitizenId(null); setSelectedHouseholdId(null) }
  const selectBiography = (citizenId: string) => setSelectedBiographyCitizenId(citizenId)
  const selectHousehold = (householdId: string) => setSelectedHouseholdId(householdId)
  const openRecords = (tab: ObserverTab) => { setRecordsTab(tab); setRecordsOpen(true) }
  const closeRecords = () => { setRecordsOpen(false); queueMicrotask(() => recordsButton.current?.focus()) }
  const openCitizenRecord = (citizenId: string) => { selectCitizen(citizenId); selectBiography(citizenId); openRecords('Citizens') }
  const updateControl = async (request: () => Promise<Status>) => { setControlBusy(true); setControlError(null); try { setStatus(await request()) } catch (reason: unknown) { setControlError(errorText(reason, 'The operational control could not be applied.')) } finally { setControlBusy(false) } }
  const connected = health?.ok === true && error === null && liveConnectionState === 'connected'; const stateLabel = status.paused ? 'Paused' : status.state.toLowerCase() === 'running' ? 'Running' : status.state; const activeProject = settlement?.activeConstructionProject; const selectedCitizen = citizens?.find(citizen => citizen.citizenId === selectedCitizenId) ?? null
  const liveLabel = liveConnectionState === 'connected' ? 'connected' : liveConnectionState === 'connecting' ? 'connecting…' : liveConnectionState === 'reconnecting' ? 'reconnecting…' : liveConnectionState === 'disconnected' ? 'disconnected' : 'unavailable'
  return <main className={`page-shell ${recordsOpen ? 'has-records-open' : ''}`}><div className="topline"><span className="mark" aria-hidden="true">✦</span><span>Observer station</span><span className="rule" /></div><header className="hero"><p className="eyebrow">A world in quiet motion</p><h1>Little <em>Ages</em></h1><p className="intro">A living miniature world.<br className="desktop-break" /> The server keeps time; this page simply looks in.</p></header><section className="status-strip" aria-label="Connection status" role="status" aria-live="polite"><span className={`status-dot ${connected ? 'is-good' : ''}`} aria-hidden="true" /><span>{error ? 'Connection unavailable' : health === null ? 'Checking the server…' : connected ? 'Server connected' : `Server: ${health.label}`}</span><span className="live-status">Live updates: {liveLabel}</span><span className="status-detail">{liveConnectionState === 'connected' ? 'Observer connected' : 'Degraded · responsive REST fallback active'}</span></section>{error && <div className="notice" role="alert">{error}. Make sure the Little Ages server is running.</div>}{mapError && <div className="notice" role="alert">Map unavailable: {mapError}.</div>}{status.error && <div className="notice" role="alert">Server reports: {status.error}</div>}
    {map === null && !error && <div className="world-loading" role="status">Preparing the living diorama…</div>}{map !== null && <SceneLoadBoundary fallback={<section className="world-viewport"><p role="alert">The 3D view could not load. Showing the 2D map; reload to retry.</p><LegacyMap map={map} structures={structures ?? []} citizens={citizens ?? []} /></section>}><Suspense fallback={<div className="world-loading" role="status">Preparing the living diorama…</div>}><WorldViewport map={map} structures={structures ?? []} citizens={citizens ?? []} settlement={settlement} worldSeed={status.worldSeed} operationalSpeed={status.operationalSpeed} worldMinute={status.worldMinute ?? 0} paused={status.paused} controlsEnabled={!recordsOpen} selectedCitizenId={selectedCitizenId} onSelectCitizen={selectCitizen} onOpenSelected={openCitizenRecord} /></Suspense></SceneLoadBoundary>}
    <section className="world-hud" aria-label="World controls"><h2 className="visually-hidden">Set the pace</h2><div><span>World time</span><strong>{status.worldMinute === null ? '—' : `Current: ${formatCalendarDate(status.worldMinute)}`}</strong></div><div><span>Population</span><strong>{formatValue(status.livingPopulation ?? settlement?.livingPopulation)}</strong></div><div className="world-hud-controls"><button type="button" onClick={() => { void updateControl(status.paused ? resumeSimulation : pauseSimulation) }} disabled={controlBusy}>{status.paused ? 'Resume' : 'Pause'}</button><label>Speed<select aria-label="Simulation speed" value={status.operationalSpeed ?? 10} onChange={event => { void updateControl(() => changeSimulationSpeed(Number(event.target.value))) }} disabled={controlBusy}>{SAFE_OPERATIONAL_SPEEDS.map(speed => <option key={speed} value={speed}>{speed} min/s</option>)}</select></label></div></section>{controlError && <div className="notice control-notice" role="alert">{controlError}</div>}
    {recordsOpen && <aside className="observer-drawer" role="dialog" aria-modal="false" aria-labelledby="observer-panel-title"><header className="observer-panel-header"><div><span className="section-kicker">Settlement ledger</span><h2 id="observer-panel-title">Observer records</h2></div><button ref={recordsCloseButton} type="button" className="observer-close" onClick={closeRecords} aria-label="Close observer records">×</button></header><nav className="observer-tabs" role="tablist" aria-label="Observer records sections">{OBSERVER_TABS.map(tab => <button key={tab} id={`observer-tab-${tab.toLowerCase()}`} type="button" role="tab" aria-controls="observer-tabpanel" aria-selected={recordsTab === tab} tabIndex={recordsTab === tab ? 0 : -1} className={recordsTab === tab ? 'is-active' : ''} onClick={() => setRecordsTab(tab)}>{tab}</button>)}</nav><div id="observer-tabpanel" className="observer-drawer-body" role="tabpanel" aria-labelledby={`observer-tab-${recordsTab.toLowerCase()}`} data-tab={recordsTab.toLowerCase()}>
    {recordsTab === 'Overview' && <>
    <section className="observation" aria-labelledby="observation-heading"><div className="section-heading"><span className="section-kicker">Current observation</span><h2 id="observation-heading">The world, as it is now</h2></div><div className="reading-grid"><article className="reading reading-primary"><span className="reading-label">World minute</span><strong>{formatMinute(status.worldMinute)}</strong><span className="reading-note">Canonical simulation time</span></article><article className="reading"><span className="reading-label">Host state</span><strong>{health === null ? 'Loading…' : stateLabel}</strong><span className="reading-note">Authoritative server</span></article></div></section>
    <section className="control-surface" aria-labelledby="control-heading"><div className="section-heading"><span className="section-kicker">Operational controls</span><h2 id="control-heading">Pacing details</h2><p className="section-description">Pause and speed controls remain in the world HUD. They affect operational pacing only, never canonical state.</p></div></section>
    <section className="settlement" aria-labelledby="settlement-heading"><div className="section-heading"><span className="section-kicker">Settlement & construction</span><h2 id="settlement-heading">The shared stores</h2></div>{settlement === null && !error && <p role="status" aria-live="polite">Loading settlement…</p>}{settlement !== null && <><div className="settlement-grid settlement-grid-expanded"><article className="reading reading-primary"><span className="reading-label">Storage</span><strong>{formatValue(settlement.storageUsed)}<small> / {formatValue(settlement.storageCapacity)}</small></strong><span className="reading-note">Used capacity</span></article><article className="reading"><span className="reading-label">Shelter</span><strong>{formatValue(settlement.shelteredPopulation)}<small> / {formatValue(settlement.shelterCapacity)}</small></strong><span className="reading-note">Housed · {formatValue(settlement.unhousedPopulation)} unhoused</span></article><article className="reading"><span className="reading-label">Buildings complete</span><strong>{formatValue(settlement.completedShelters + settlement.completedStockpiles + settlement.completedWorkshops)}</strong><span className="reading-note">{settlement.completedShelters} shelters · {settlement.completedStockpiles} stockpiles · {settlement.completedWorkshops} workshops</span></article><article className="reading"><span className="reading-label">Exposure grace</span><strong>{formatMinute(settlement.exposureGraceUntilMinute)}</strong><span className="reading-note">Until this world minute</span></article></div><div className="stock-grid" aria-label="Settlement resources"><span>Food <strong>{formatValue(settlement.foodStored)}</strong></span><span>Wood <strong>{formatValue(settlement.woodStored)}</strong></span><span>Stone <strong>{formatValue(settlement.stoneStored)}</strong></span></div><div className="social-summary" aria-label="Current social summary"><span>Households <strong>{formatValue(settlement.householdCount)}</strong> · active {formatValue(settlement.activeHouseholdCount)}</span><span>Partnerships <strong>{formatValue(settlement.partnershipCount)}</strong> · relationships {formatValue(settlement.relationshipCount)}</span><span>Friends {formatValue(settlement.friendCount)} · rivals {formatValue(settlement.rivalCount)}</span><span>Young children {formatValue(settlement.youngChildCount)} · children {formatValue(settlement.childCount)} · adolescents {formatValue(settlement.adolescentCount)} · adults {formatValue(settlement.adultCount)} · elders {formatValue(settlement.elderCount)}</span></div>{activeProject !== null && activeProject !== undefined && <article className="active-project"><div><span className="section-kicker">Active construction</span><h3>{structureLabel(activeProject)}</h3></div><div className="project-progress"><span>Materials: wood {activeProject.deliveredWood}/{activeProject.requiredWood} · stone {activeProject.deliveredStone}/{activeProject.requiredStone}</span><progress value={activeProject.completedWork} max={activeProject.requiredWork} aria-label={`${activeProject.type} work progress`} /><span>{formatValue(activeProject.completedWork)} / {formatValue(activeProject.requiredWork)} work · {percent(activeProject.completedWork, activeProject.requiredWork)}%</span></div></article>}</>}</section>
    </>}
    {recordsTab === 'Buildings' && <>
    <section className="structures" aria-labelledby="structures-heading"><div className="section-heading"><span className="section-kicker">Building ledger</span><h2 id="structures-heading">Shelter, stores, and workshop</h2></div>{structures === null && !error && <p role="status" aria-live="polite">Loading structures…</p>}{activeProject !== null && activeProject !== undefined && <article className="active-project"><div><span className="section-kicker">Active construction</span><h3>{structureLabel(activeProject)}</h3></div><div className="project-progress"><span>Materials: wood {activeProject.deliveredWood}/{activeProject.requiredWood} · stone {activeProject.deliveredStone}/{activeProject.requiredStone}</span><progress value={activeProject.completedWork} max={activeProject.requiredWork} aria-label={`${activeProject.type} work progress`} /><span>{formatValue(activeProject.completedWork)} / {formatValue(activeProject.requiredWork)} work · {percent(activeProject.completedWork, activeProject.requiredWork)}%</span></div></article>}{structures !== null && structures.length === 0 && <p>No structures have been raised.</p>}{structures && structures.length > 0 && <div className="structure-grid">{structures.map(structure => <article className={`structure-card ${structure.status === 'Complete' ? 'is-complete' : ''}`} key={structure.structureId}><div className="structure-card-heading"><h3>{structure.type}</h3><span>{structure.status === 'Complete' ? 'Complete' : 'Building'}</span></div><p>At ({structure.location.x}, {structure.location.y}) · started {formatMinute(structure.startedMinute)}</p><dl className="structure-details"><div><dt>Wood</dt><dd>{formatValue(structure.deliveredWood)} / {formatValue(structure.requiredWood)}</dd></div><div><dt>Stone</dt><dd>{formatValue(structure.deliveredStone)} / {formatValue(structure.requiredStone)}</dd></div><div><dt>Work</dt><dd>{formatValue(structure.completedWork)} / {formatValue(structure.requiredWork)}</dd></div><div><dt>Occupants</dt><dd>{formatValue(structure.currentOccupantIds.length)}</dd></div></dl></article>)}</div>}</section>
    </>}
    {recordsTab === 'Citizens' && <>
    <section className="households" aria-labelledby="households-heading"><div className="section-heading"><span className="section-kicker">Households</span><h2 id="households-heading">Homes and kin</h2></div>{households === null && !error && <p role="status">Loading households…</p>}{households !== null && households.length === 0 && <p>No households have formed.</p>}{households && households.length > 0 && <div className="household-grid">{households.map(household => <article className="household-card" key={household.householdId}><h3>Household {household.householdId}</h3><p>{household.livingMemberIds.length} living members · {household.dissolvedMinute === null ? 'Active' : 'Dissolved'}</p><button type="button" className="observer-button" onClick={() => selectHousehold(household.householdId)}>View household details</button></article>)}</div>}{householdError && <p className="notice" role="alert">Household unavailable: {householdError}</p>}{selectedHousehold !== null && <HouseholdDetails household={selectedHousehold} />}</section>
    <section className="citizens" aria-labelledby="citizens-heading"><div className="section-heading"><span className="section-kicker">Citizens</span><h2 id="citizens-heading">Lives in motion</h2></div>{citizens === null && !error && <p role="status" aria-live="polite">Loading citizens…</p>}{citizens !== null && citizens.length === 0 && <p>No citizens are available.</p>}{citizens && citizens.length > 0 && <div className="citizen-browser"><div className="citizen-list" role="list" aria-label="Citizens">{citizens.map(citizen => <button type="button" role="listitem" aria-label={`${citizen.name}, ${citizen.lifeStage}, ${citizen.currentAction}`} className={citizen.citizenId === selectedCitizenId ? 'is-selected' : ''} key={citizen.citizenId} onClick={() => selectCitizen(citizen.citizenId)}><strong>{citizen.name}</strong><span>{citizen.lifeStage} · {citizen.occupation}</span><small>{citizen.currentAction}</small></button>)}</div>{selectedCitizen !== null && <article className={`citizen-card citizen-record ${selectedCitizen.isAlive ? '' : 'is-dead'}`}><h3>{selectedCitizen.name}</h3><p>{selectedCitizen.age} years · {selectedCitizen.lifeStage} · {selectedCitizen.isAlive ? 'Alive' : 'Dead'} · {selectedCitizen.occupation}</p><p>At ({selectedCitizen.location.x}, {selectedCitizen.location.y})</p><dl className="citizen-details"><div><dt>Health</dt><dd>{formatValue(selectedCitizen.health)}</dd></div><div><dt>Hunger</dt><dd>{formatValue(selectedCitizen.hunger)}</dd></div><div><dt>Rest</dt><dd>{formatValue(selectedCitizen.rest)}</dd></div><div><dt>Shelter need</dt><dd>{formatValue(selectedCitizen.shelter)}</dd></div><div><dt>Social need</dt><dd>{formatValue(selectedCitizen.social)}</dd></div><div><dt>Current action</dt><dd>{selectedCitizen.currentAction}</dd></div><div><dt>Phase</dt><dd>{selectedCitizen.actionPhase}</dd></div>{selectedCitizen.deathCause !== null && <div><dt>Death cause</dt><dd>{selectedCitizen.deathCause}</dd></div>}{selectedCitizen.homeStructureId !== null && <div><dt>Home</dt><dd>Structure {selectedCitizen.homeStructureId}</dd></div>}{selectedCitizen.targetCitizenId !== null && <div><dt>Social target</dt><dd>Citizen {selectedCitizen.targetCitizenId}</dd></div>}{selectedCitizen.targetStructureId !== null && <div><dt>Build / haul target</dt><dd>Structure {selectedCitizen.targetStructureId}</dd></div>}{selectedCitizen.targetResourceNodeId !== null && <div><dt>Target node</dt><dd>{selectedCitizen.targetResourceNodeId}</dd></div>}{selectedCitizen.carriedResource !== null && <div><dt>Carrying</dt><dd>{selectedCitizen.carriedResource} · {formatValue(selectedCitizen.carriedQuantity)}</dd></div>}</dl><div className="citizen-record-actions"><button type="button" className="observer-button" onClick={() => setRelationshipCitizenId(selectedCitizen.citizenId)}>View relationship details</button>{selectedCitizen.householdId !== null && <button type="button" className="observer-button" onClick={() => selectHousehold(selectedCitizen.householdId!)}>View household details</button>}<button type="button" className="observer-button" onClick={() => selectBiography(selectedCitizen.citizenId)}>View biography</button></div><DemographicsDetails citizen={selectedCitizen} /><FamilyDetails citizen={selectedCitizen} /></article>}</div>}</section>{selectedCitizen !== null && relationshipCitizenId === selectedCitizen.citizenId && <RelationshipDetails citizen={selectedCitizen} relationships={relationships} loading={relationshipsLoading} error={relationshipError} />}<BiographySurface biography={biography} loading={biographyLoading} error={biographyError} />
    </>}
    {recordsTab === 'History' && <HistorySurface citizens={citizens ?? []} refreshToken={liveRefreshRevision} />}
    {recordsTab === 'Statistics' && <StatisticsSurface samples={statistics} loading={statisticsLoading} error={statisticsError} hasMore={statisticsHasMore} refreshToken={liveRefreshRevision} onQuery={requestStatistics} onRefresh={refreshStatistics} />}</div></aside>}<button ref={recordsButton} type="button" className="observer-records-toggle" aria-expanded={recordsOpen} onClick={() => recordsOpen ? closeRecords() : openRecords('Overview')}>{recordsOpen ? 'Close records' : 'Observer records'}</button><footer><span>Little Ages · Living observer</span><span>Controls affect operational pacing only.</span></footer>
  </main>
}
