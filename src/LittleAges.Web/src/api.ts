export type Health = { ok: boolean; label: string }

export type Status = {
  state: string
  worldMinute: number | null
  pendingEventCount: number | null
  worldSeed: string | null
  error: string | null
  paused: boolean
  operationalSpeed: number | null
  population?: number | null
  totalPopulation?: number | null
  livingPopulation?: number | null
  deadPopulation?: number | null
}

export type ResourceType = 'Food' | 'Wood' | 'Stone'
export type CitizenAction = typeof ACTIONS[number]
export type CitizenActionPhase = typeof ACTION_PHASES[number]

export type Citizen = {
  citizenId: string
  name: string
  age: number
  lifeStage: string
  location: { x: number; y: number }
  health: number
  currentAction: CitizenAction
  actionSequence: number
  actionStartedMinute: number | null
  actionCompletesMinute: number | null
  target: { x: number; y: number } | null
  isAlive: boolean
  deathMinute: number | null
  deathCause: string | null
  hunger: number | null
  rest: number | null
  shelter: number | null
  social: number | null
  actionPhase: CitizenActionPhase
  carriedResource: ResourceType | null
  carriedQuantity: number | null
  targetResourceNodeId: string | null
  homeStructureId: string | null
  targetStructureId: string | null
  occupation: CitizenOccupation
  lifetimeWorkActivity: WorkActivity
  founderOrdinal: number | null
  parentAId: string | null
  parentBId: string | null
  partnerId: string | null
  householdId: string | null
  childrenIds: string[]
  targetCitizenId: string | null
  movementPlan: CitizenMovementPlan | null
}

export type CitizenMovementWaypoint = { x: number; y: number; arriveMinute: number }
export type CitizenMovementPlan = { actionSequence: number; observedMinute: number; segmentStartedMinute?: number; waypoints: CitizenMovementWaypoint[] }

export type CitizenOccupation = 'Farmer' | 'Woodcutter' | 'Generalist' | 'Forager' | 'Lumberjack' | 'Stoneworker' | 'Builder' | 'Hauler'
export type WorkActivity = { foragingMinutes: number; woodcuttingMinutes: number; stoneworkingMinutes: number; constructionMinutes: number; haulingMinutes: number }

export type SettlementResourceQuantity = { resourceType: ResourceType; quantity: number }
export type SettlementResource = { resourceNodeId: string; resourceType: ResourceType; currentQuantity: number }

export type Settlement = {
  foodStored: number
  woodStored: number
  stoneStored: number
  livingPopulation: number
  deadPopulation: number
  totalPopulation: number
  remainingResources: SettlementResourceQuantity[]
  resources: SettlementResource[]
  storageCapacity: number
  storageUsed: number
  shelterCapacity: number
  shelteredPopulation: number
  unhousedPopulation: number
  completedShelters: number
  completedStockpiles: number
  completedWorkshops: number
  completedFarms?: number
  completedGranaries?: number
  completedMarketplaces?: number
  exposureGraceUntilMinute: number
  activeConstructionProject: Structure | null
  householdCount: number
  activeHouseholdCount: number
  partnershipCount: number
  relationshipCount: number
  friendCount: number
  rivalCount: number
  youngChildCount: number
  childCount: number
  adolescentCount: number
  adultCount: number
  elderCount: number
}

export type Relationship = {
  otherCitizenId: string
  otherCitizenName: string
  familiarity: number
  affinity: number
  trust: number
  conflict: number
  lastInteractionMinute: number
  interactionCount: number
  label: RelationshipLabel
}

export type RelationshipLabel = 'Partner' | 'Family' | 'Rival' | 'Close Friend' | 'Friend' | 'Acquaintance' | 'Stranger'

export type Household = {
  householdId: string
  createdMinute: number
  dissolvedMinute: number | null
  dwellingStructureId: string | null
  memberIds: string[]
  livingMemberIds: string[]
  partnerPair: string[] | null
  childrenIds: string[]
}

export type StructureType = 'Shelter' | 'Stockpile' | 'Workshop' | 'Farm' | 'Granary' | 'Marketplace'
export type StructureStatus = 'UnderConstruction' | 'Complete'
export type StructureContribution = { citizenId: string; constructionWork: number; woodDelivered: number; stoneDelivered: number }
export type Structure = {
  structureId: string
  type: StructureType
  cropStage?: string | null
  status: StructureStatus
  location: { x: number; y: number }
  startedMinute: number
  completedMinute: number | null
  requiredWood: number
  deliveredWood: number
  requiredStone: number
  deliveredStone: number
  requiredWork: number
  completedWork: number
  condition: number
  capacity: number | null
  storageBonus: number | null
  constructionMultiplierBasisPoints: number | null
  currentOccupantIds: string[]
  contributions: StructureContribution[]
}

export type MapResource = {
  resourceNodeId: string
  resourceType: ResourceType
  location: { x: number; y: number }
  maximumQuantity: number
  regenerationPotential: number
}

export type Map = {
  width: number
  height: number
  terrain: number[]
  elevation: number[]
  resources: MapResource[]
  startingSite: { x: number; y: number }
}

export const HISTORICAL_EVENT_TYPES = ['WorldCreated', 'SettlementFounded', 'CitizenBorn', 'CitizenDied', 'PartnershipFormed', 'FriendshipFormed', 'RivalryFormed', 'HouseholdCreated', 'StructureStarted', 'StructureCompleted', 'PopulationMilestone', 'ResourceShortageStarted', 'ResourceShortageEnded', 'CitizenSpecializationChanged', 'SeasonStarted'] as const
export type HistoricalEventType = typeof HISTORICAL_EVENT_TYPES[number]
export const HISTORICAL_IMPORTANCES = ['Debug', 'Routine', 'Personal', 'Notable', 'Major', 'Historic'] as const
export type HistoricalImportance = typeof HISTORICAL_IMPORTANCES[number]
export const HISTORICAL_ORIGINS = ['Live', 'MigrationBackfill'] as const
export type HistoricalEventOrigin = typeof HISTORICAL_ORIGINS[number]
export const MEMORY_TYPES = ['ChildBorn', 'PartnerDied', 'PartnershipFormed', 'FriendshipFormed', 'RivalryFormed', 'StructureCompleted'] as const
export type MemoryType = typeof MEMORY_TYPES[number]

export type HistoricalCitizenLink = { eventId: string; citizenId: string; role: string }
export type HistoricalStructureLink = { eventId: string; structureId: string; role: string }
export type HistoricalEvent = {
  eventId: string
  historicalEventId: string
  worldMinute: number
  eventType: HistoricalEventType
  importance: HistoricalImportance
  origin: HistoricalEventOrigin
  location: { x: number; y: number } | null
  payloadJson: string
  schemaVersion: number
  summary: string
  citizenLinks: HistoricalCitizenLink[]
  structureLinks: HistoricalStructureLink[]
}

export type HistoryState = {
  historyStartMinute: number
  historyStartEventId: string
  periodStartMinute: number
  birthsSinceSample: number
  deathsSinceSample: number
  foodProducedSinceSample: number
  foodConsumedSinceSample: number
  activeFoodShortage: boolean
  populationMilestoneWatermark: number
}

export type StatisticsSample = {
  worldMinute: number
  periodStartMinute: number
  population: number
  birthsPeriod: number
  deathsPeriod: number
  foodStored: number
  foodProducedPeriod: number
  foodConsumedPeriod: number
  woodStored: number
  stoneStored: number
  shelterCapacity: number
  averageHealth: number
  averageHunger: number
}

export type CitizenMemory = {
  citizenId: string
  eventId: string
  memoryType: MemoryType
  importance: HistoricalImportance
  emotionalValence: number
  createdMinute: number
}

export type CitizenBiography = {
  citizen: Citizen
  events: HistoricalEvent[]
  memories: CitizenMemory[]
  parentIds: string[]
  partnerId: string | null
  childrenIds: string[]
  birthMinute: number | null
  deathMinute: number | null
  deathCause: string | null
}

export type HistoryQuery = {
  fromMinute?: number
  toMinute?: number
  eventType?: HistoricalEventType
  minimumImportance?: number
  citizenId?: string
  familyCitizenId?: string
  structureId?: string
  beforeEventId?: string
  limit?: number
}

export type StatisticsQuery = Pick<HistoryQuery, 'fromMinute' | 'toMinute' | 'limit'>

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

export function parseHealth(value: unknown): Health {
  if (typeof value === 'string') {
    const text = value.trim()
    return { ok: text.toLowerCase() === 'healthy' || text.toLowerCase() === 'ok', label: text || 'Unknown' }
  }
  if (isRecord(value)) {
    const label = typeof value.status === 'string' ? value.status : typeof value.message === 'string' ? value.message : 'Unknown'
    return { ok: label.toLowerCase() === 'healthy' || label.toLowerCase() === 'ok', label }
  }
  return { ok: false, label: 'Unknown' }
}

export function parseStatus(value: unknown): Status {
  const data = isRecord(value) ? value : {}
  const result: Status = {
    state: typeof data.state === 'string' ? data.state : 'Unknown',
    worldMinute: typeof data.worldMinute === 'number' && Number.isFinite(data.worldMinute) ? data.worldMinute : null,
    pendingEventCount: typeof data.pendingEventCount === 'number' && Number.isFinite(data.pendingEventCount) ? data.pendingEventCount : null,
    worldSeed: typeof data.worldSeed === 'string' ? data.worldSeed : null,
    error: typeof data.error === 'string' ? data.error : null,
    paused: typeof data.paused === 'boolean' ? data.paused : false,
    operationalSpeed: typeof data.operationalSpeed === 'number' && Number.isFinite(data.operationalSpeed) && data.operationalSpeed >= 0 ? data.operationalSpeed : null,
  }
  for (const field of ['population', 'totalPopulation', 'livingPopulation', 'deadPopulation'] as const) {
    if (field in data) result[field] = parseOptionalNonNegativeInteger(data[field])
  }
  return result
}

async function get(path: string): Promise<unknown> {
  const response = await fetch(path)
  if (!response.ok) throw new Error(`Request failed (${response.status})`)
  const text = await response.text()
  try { return JSON.parse(text) as unknown } catch { return text }
}

async function post(path: string, body?: unknown): Promise<unknown> {
  const response = await fetch(path, body === undefined ? { method: 'POST' } : { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
  if (!response.ok) throw new Error(`Request failed (${response.status})`)
  const text = await response.text()
  try { return JSON.parse(text) as unknown } catch { return text }
}

export async function fetchHealth(): Promise<Health> { return parseHealth(await get('/api/v1/health')) }
export async function fetchStatus(): Promise<Status> { return parseStatus(await get('/api/v1/status')) }

export const SAFE_OPERATIONAL_SPEEDS = [1, 5, 10, 50] as const
export const MAX_OPERATIONAL_SPEED = 1000

export async function pauseSimulation(): Promise<Status> { return parseStatus(await post('/api/v1/control/pause')) }
export async function resumeSimulation(): Promise<Status> { return parseStatus(await post('/api/v1/control/resume')) }
export async function changeSimulationSpeed(speed: number): Promise<Status> {
  if (!Number.isFinite(speed) || speed <= 0 || speed > MAX_OPERATIONAL_SPEED) throw new Error(`Speed must be finite, positive, and no greater than ${MAX_OPERATIONAL_SPEED}.`)
  return parseStatus(await post('/api/v1/control/speed', { speed }))
}

const HISTORICAL_CITIZEN_ROLES = ['subject', 'parent', 'partner', 'founder', 'participant', 'member', 'contributor'] as const
const HISTORICAL_STRUCTURE_ROLES = ['subject'] as const

function parseRequiredInteger(value: unknown, message: string, minimum = Number.MIN_SAFE_INTEGER, maximum = Number.MAX_SAFE_INTEGER): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < minimum || value > maximum) throw new Error(message)
  return value
}

function parseHistoryEnum<T extends readonly string[]>(value: unknown, values: T, message: string): T[number] {
  if (typeof value !== 'string' || !values.includes(value)) throw new Error(message)
  return value as T[number]
}

function parseCanonicalPayload(value: unknown): string {
  if (typeof value !== 'string' || value.trim() === '') throw new Error('The server returned an invalid historical payload.')
  try {
    const parsed: unknown = JSON.parse(value)
    if (!isRecord(parsed) || Array.isArray(parsed)) throw new Error('not an object')
    return value
  } catch {
    throw new Error('The server returned an invalid historical payload.')
  }
}

function compareDecimalIds(first: string, second: string): number {
  const a = BigInt(first)
  const b = BigInt(second)
  return a < b ? -1 : a > b ? 1 : 0
}

function parseHistoricalCitizenLink(value: unknown, eventId: string): HistoricalCitizenLink {
  if (!isRecord(value) || value.eventId !== eventId || typeof value.role !== 'string' || !HISTORICAL_CITIZEN_ROLES.includes(value.role as typeof HISTORICAL_CITIZEN_ROLES[number])) throw new Error('The server returned an invalid historical citizen link.')
  return { eventId, citizenId: parsePositiveDecimalId(value.citizenId, 'The server returned an invalid historical citizen link citizen ID.'), role: value.role }
}

function parseHistoricalStructureLink(value: unknown, eventId: string): HistoricalStructureLink {
  if (!isRecord(value) || value.eventId !== eventId || typeof value.role !== 'string' || !HISTORICAL_STRUCTURE_ROLES.includes(value.role as typeof HISTORICAL_STRUCTURE_ROLES[number])) throw new Error('The server returned an invalid historical structure link.')
  return { eventId, structureId: parsePositiveDecimalId(value.structureId, 'The server returned an invalid historical structure link structure ID.'), role: value.role }
}

export function parseHistoricalEvent(value: unknown): HistoricalEvent {
  if (!isRecord(value) || typeof value.eventId !== 'string' || value.historicalEventId !== value.eventId || value.location === undefined || !Array.isArray(value.citizenLinks) || !Array.isArray(value.structureLinks) || typeof value.summary !== 'string') throw new Error('The server returned an invalid historical event.')
  const eventId = parsePositiveDecimalId(value.eventId, 'The server returned an invalid historical event ID.')
  const worldMinute = parseRequiredNonNegativeInteger(value.worldMinute, 'The server returned an invalid historical event minute.')
  const eventType = parseHistoryEnum(value.eventType, HISTORICAL_EVENT_TYPES, 'The server returned an invalid historical event type.')
  const importance = parseHistoryEnum(value.importance, HISTORICAL_IMPORTANCES, 'The server returned an invalid historical event importance.')
  const origin = parseHistoryEnum(value.origin, HISTORICAL_ORIGINS, 'The server returned an invalid historical event origin.')
  const location = value.location === null ? null : parseCoordinate(value.location, 'The server returned an invalid historical event location.')
  const schemaVersion = parseRequiredNonNegativeInteger(value.schemaVersion, 'The server returned an invalid historical event schema version.')
  if (schemaVersion !== 1) throw new Error('The server returned an unsupported historical event schema version.')
  const citizenLinks = value.citizenLinks.map(link => parseHistoricalCitizenLink(link, eventId))
  const structureLinks = value.structureLinks.map(link => parseHistoricalStructureLink(link, eventId))
  for (let index = 1; index < citizenLinks.length; index += 1) {
    const previous = citizenLinks[index - 1]
    const current = citizenLinks[index]
    if (compareDecimalIds(previous.citizenId, current.citizenId) >= 0 || (previous.citizenId === current.citizenId && previous.role >= current.role)) throw new Error('The server returned historical citizen links out of canonical order.')
  }
  for (let index = 1; index < structureLinks.length; index += 1) {
    const previous = structureLinks[index - 1]
    const current = structureLinks[index]
    if (compareDecimalIds(previous.structureId, current.structureId) >= 0 || (previous.structureId === current.structureId && previous.role >= current.role)) throw new Error('The server returned historical structure links out of canonical order.')
  }
  return {
    eventId,
    historicalEventId: eventId,
    worldMinute,
    eventType,
    importance,
    origin,
    location,
    payloadJson: parseCanonicalPayload(value.payloadJson),
    schemaVersion,
    summary: value.summary,
    citizenLinks,
    structureLinks,
  }
}

function parseHistoricalEventList(value: unknown): HistoricalEvent[] {
  if (!Array.isArray(value)) throw new Error('The server returned an invalid historical event list.')
  const events = value.map(parseHistoricalEvent)
  const ids = new Set<string>()
  for (const event of events) {
    if (ids.has(event.eventId)) throw new Error('The server returned duplicate historical events.')
    ids.add(event.eventId)
  }
  return events
}

export function parseHistory(value: unknown): HistoricalEvent[] {
  const events = parseHistoricalEventList(value)
  for (let index = 1; index < events.length; index += 1) {
    const previous = events[index - 1]
    const current = events[index]
    if (previous.worldMinute < current.worldMinute || (previous.worldMinute === current.worldMinute && compareDecimalIds(previous.eventId, current.eventId) <= 0)) throw new Error('The server returned history out of canonical order.')
  }
  return events
}

function parseBiographyEvents(value: unknown): HistoricalEvent[] {
  const events = parseHistoricalEventList(value)
  for (let index = 1; index < events.length; index += 1) {
    const previous = events[index - 1]
    const current = events[index]
    if (previous.worldMinute > current.worldMinute || (previous.worldMinute === current.worldMinute && compareDecimalIds(previous.eventId, current.eventId) >= 0)) throw new Error('The server returned biography events out of canonical order.')
  }
  return events
}

export function parseCitizenMemory(value: unknown): CitizenMemory {
  if (!isRecord(value)) throw new Error('The server returned an invalid citizen memory.')
  const citizenId = parsePositiveDecimalId(value.citizenId, 'The server returned an invalid citizen memory citizen ID.')
  const eventId = parsePositiveDecimalId(value.eventId, 'The server returned an invalid citizen memory event ID.')
  const memoryType = parseHistoryEnum(value.memoryType, MEMORY_TYPES, 'The server returned an invalid citizen memory type.')
  const importance = parseHistoryEnum(value.importance, HISTORICAL_IMPORTANCES, 'The server returned an invalid citizen memory importance.')
  const emotionalValence = parseRequiredInteger(value.emotionalValence, 'The server returned an invalid citizen memory valence.', -10000, 10000)
  const createdMinute = parseRequiredNonNegativeInteger(value.createdMinute, 'The server returned an invalid citizen memory minute.')
  return { citizenId, eventId, memoryType, importance, emotionalValence, createdMinute }
}

function parseCitizenMemories(value: unknown, citizenId?: string): CitizenMemory[] {
  if (!Array.isArray(value)) throw new Error('The server returned an invalid citizen memory list.')
  const memories = value.map(parseCitizenMemory)
  const keys = new Set<string>()
  for (const memory of memories) {
    if (citizenId !== undefined && memory.citizenId !== citizenId) throw new Error('The server returned a memory for another citizen.')
    const key = `${memory.citizenId}:${memory.eventId}:${memory.memoryType}`
    if (keys.has(key)) throw new Error('The server returned duplicate citizen memories.')
    keys.add(key)
  }
  return memories
}

export function parseBiography(value: unknown): CitizenBiography {
  if (!isRecord(value) || value.citizen === undefined || value.events === undefined || value.memories === undefined || !Array.isArray(value.parentIds) || !Array.isArray(value.childrenIds)) throw new Error('The server returned an invalid citizen biography.')
  const citizenList = parseCitizens([value.citizen])
  if (citizenList.length !== 1) throw new Error('The server returned an invalid citizen biography citizen.')
  const citizen = citizenList[0]
  const parentIds = parseCanonicalIdList(value.parentIds, 'The server returned invalid biography parent IDs.')
  const childrenIds = parseCanonicalIdList(value.childrenIds, 'The server returned invalid biography children IDs.')
  const partnerId = parseNullablePositiveDecimalId(value.partnerId, 'The server returned an invalid biography partner ID.')
  const birthMinute = parseNullableSignedInteger(value.birthMinute, 'The server returned an invalid biography birth minute.')
  const deathMinute = parseNullableNonNegativeInteger(value.deathMinute, 'The server returned an invalid biography death minute.')
  const deathCause = parseNullableString(value.deathCause, 'The server returned an invalid biography death cause.')
  if (deathMinute !== null && birthMinute !== null && deathMinute < birthMinute) throw new Error('The server returned an incoherent biography timeline.')
  const events = parseBiographyEvents(value.events)
  const memories = parseCitizenMemories(value.memories, citizen.citizenId)
  return { citizen, events, memories, parentIds, partnerId, childrenIds, birthMinute, deathMinute, deathCause }
}

export function parseStatistics(value: unknown): StatisticsSample[] {
  if (!Array.isArray(value)) throw new Error('The server returned an invalid statistics list.')
  const samples = value.map(item => {
    if (!isRecord(item)) throw new Error('The server returned an invalid statistics sample.')
    return {
      worldMinute: parseRequiredNonNegativeInteger(item.worldMinute, 'The server returned an invalid statistics sample minute.'),
      periodStartMinute: parseRequiredNonNegativeInteger(item.periodStartMinute, 'The server returned an invalid statistics period minute.'),
      population: parseRequiredNonNegativeInteger(item.population, 'The server returned an invalid statistics population.'),
      birthsPeriod: parseRequiredNonNegativeInteger(item.birthsPeriod, 'The server returned an invalid statistics births.'),
      deathsPeriod: parseRequiredNonNegativeInteger(item.deathsPeriod, 'The server returned an invalid statistics deaths.'),
      foodStored: parseRequiredNonNegativeInteger(item.foodStored, 'The server returned an invalid statistics food stock.'),
      foodProducedPeriod: parseRequiredNonNegativeInteger(item.foodProducedPeriod, 'The server returned an invalid statistics food production.'),
      foodConsumedPeriod: parseRequiredNonNegativeInteger(item.foodConsumedPeriod, 'The server returned an invalid statistics food consumption.'),
      woodStored: parseRequiredNonNegativeInteger(item.woodStored, 'The server returned an invalid statistics wood stock.'),
      stoneStored: parseRequiredNonNegativeInteger(item.stoneStored, 'The server returned an invalid statistics stone stock.'),
      shelterCapacity: parseRequiredNonNegativeInteger(item.shelterCapacity, 'The server returned an invalid statistics shelter capacity.'),
      averageHealth: parseRequiredInteger(item.averageHealth, 'The server returned an invalid statistics health.', 0, 10000),
      averageHunger: parseRequiredInteger(item.averageHunger, 'The server returned an invalid statistics hunger.', 0, 10000),
    }
  })
  const minutes = new Set<number>()
  for (let index = 0; index < samples.length; index += 1) {
    if (minutes.has(samples[index].worldMinute) || (index > 0 && samples[index - 1].worldMinute >= samples[index].worldMinute)) throw new Error('The server returned statistics out of canonical order.')
    if (samples[index].periodStartMinute > samples[index].worldMinute) throw new Error('The server returned an incoherent statistics period.')
    minutes.add(samples[index].worldMinute)
  }
  return samples
}

function queryInteger(value: number | undefined, name: string, minimum = 0, maximum = Number.MAX_SAFE_INTEGER): string | undefined {
  if (value === undefined) return undefined
  return String(parseRequiredInteger(value, `Invalid ${name}.`, minimum, maximum))
}

function queryId(value: string | undefined, name: string): string | undefined {
  return value === undefined ? undefined : parsePositiveDecimalId(value, `Invalid ${name}.`)
}

export function buildHistoryQuery(query: HistoryQuery = {}): string {
  const params = new URLSearchParams()
  const fromMinute = queryInteger(query.fromMinute, 'fromMinute')
  const toMinute = queryInteger(query.toMinute, 'toMinute')
  if (fromMinute !== undefined && toMinute !== undefined && Number(query.fromMinute) > Number(query.toMinute)) throw new Error('Invalid minute range.')
  if (fromMinute !== undefined) params.set('fromMinute', fromMinute)
  if (toMinute !== undefined) params.set('toMinute', toMinute)
  if (query.eventType !== undefined) params.set('eventType', parseHistoryEnum(query.eventType, HISTORICAL_EVENT_TYPES, 'Invalid eventType.'))
  params.set('minimumImportance', queryInteger(query.minimumImportance ?? 2, 'minimumImportance', 0, 5)!)
  const citizenId = queryId(query.citizenId, 'citizenId')
  const familyCitizenId = queryId(query.familyCitizenId, 'familyCitizenId')
  const structureId = queryId(query.structureId, 'structureId')
  const beforeEventId = queryId(query.beforeEventId, 'beforeEventId')
  if (citizenId !== undefined) params.set('citizenId', citizenId)
  if (familyCitizenId !== undefined) params.set('familyCitizenId', familyCitizenId)
  if (structureId !== undefined) params.set('structureId', structureId)
  if (beforeEventId !== undefined) params.set('beforeEventId', beforeEventId)
  params.set('limit', queryInteger(query.limit ?? 50, 'limit', 1, 100)!)
  return params.toString()
}

export function buildStatisticsQuery(query: StatisticsQuery = {}): string {
  const params = new URLSearchParams()
  const fromMinute = queryInteger(query.fromMinute, 'fromMinute')
  const toMinute = queryInteger(query.toMinute, 'toMinute')
  if (fromMinute !== undefined && toMinute !== undefined && Number(query.fromMinute) > Number(query.toMinute)) throw new Error('Invalid minute range.')
  if (fromMinute !== undefined) params.set('fromMinute', fromMinute)
  if (toMinute !== undefined) params.set('toMinute', toMinute)
  params.set('limit', queryInteger(query.limit ?? 50, 'limit', 1, 100)!)
  return params.toString()
}

export async function fetchHistory(query: HistoryQuery = {}): Promise<HistoricalEvent[]> {
  const suffix = buildHistoryQuery(query)
  return parseHistory(await get(`/api/v1/history?${suffix}`))
}

export async function fetchHistoryEvent(eventId: string): Promise<HistoricalEvent> {
  return parseHistoricalEvent(await get(`/api/v1/history/${encodeURIComponent(parsePositiveDecimalId(eventId, 'Invalid historical event ID.'))}`))
}

export async function fetchBiography(citizenId: string): Promise<CitizenBiography> {
  return parseBiography(await get(`/api/v1/citizens/${encodeURIComponent(parsePositiveDecimalId(citizenId, 'Invalid citizen ID.'))}/biography`))
}

export async function fetchStatistics(query: StatisticsQuery = {}): Promise<StatisticsSample[]> {
  const suffix = buildStatisticsQuery(query)
  return parseStatistics(await get(`/api/v1/statistics?${suffix}`))
}

const ACTIONS = ['None', 'Idle', 'Rest', 'Wander', 'Explore', 'Eat', 'GatherFood', 'GatherWood', 'GatherStone', 'Dead', 'HaulConstruction', 'Build', 'Socialize', 'WorkFarm', 'HaulHarvest', 'TradeDelivery'] as const
const ACTION_PHASES = ['None', 'TravelToTarget', 'Perform', 'ReturnToStockpile', 'TravelToStockpile', 'TransportToConstruction', 'WaitingForStorage'] as const
const RESOURCE_TYPES = ['Food', 'Wood', 'Stone'] as const
const OCCUPATIONS = ['Farmer', 'Woodcutter', 'Generalist', 'Forager', 'Lumberjack', 'Stoneworker', 'Builder', 'Hauler'] as const
const STRUCTURE_TYPES = ['Shelter', 'Stockpile', 'Workshop', 'Farm', 'Granary', 'Marketplace'] as const
const STRUCTURE_STATUSES = ['UnderConstruction', 'Complete'] as const
const RELATIONSHIP_LABELS = ['Partner', 'Family', 'Rival', 'Close Friend', 'Friend', 'Acquaintance', 'Stranger'] as const

function parseOptionalNonNegativeInteger(value: unknown): number | null {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? value : null
}

function parseRequiredNonNegativeInteger(value: unknown, message: string): number {
  const parsed = parseOptionalNonNegativeInteger(value)
  if (parsed === null) throw new Error(message)
  return parsed
}

function parseNullableNonNegativeInteger(value: unknown, message: string): number | null {
  if (value === null || value === undefined) return null
  return parseRequiredNonNegativeInteger(value, message)
}

function parseNullableSignedInteger(value: unknown, message: string): number | null {
  if (value === null || value === undefined) return null
  return parseRequiredInteger(value, message)
}

function parseActionMinute(value: unknown, message: string): number | null {
  if (value === null || value === undefined) return null
  // WorldMinute is a value object on the authoritative wire contract. Retain
  // numeric support for pre-M7 observers while normalizing both forms.
  if (typeof value === 'number') return parseRequiredNonNegativeInteger(value, message)
  if (!isRecord(value) || Object.keys(value).length !== 1 || !('value' in value)) throw new Error(message)
  return parseRequiredNonNegativeInteger(value.value, message)
}

function parseNullableString(value: unknown, message: string): string | null {
  if (value === null || value === undefined) return null
  if (typeof value !== 'string') throw new Error(message)
  return value
}

function parseNullableResource(value: unknown): ResourceType | null {
  if (value === null || value === undefined) return null
  if (typeof value !== 'string' || !RESOURCE_TYPES.includes(value as ResourceType)) throw new Error('The server returned an invalid resource type.')
  return value as ResourceType
}

function parsePositiveDecimalId(value: unknown, message: string): string {
  if (typeof value !== 'string' || !/^[1-9]\d*$/.test(value)) throw new Error(message)
  return value
}

function parseNullablePositiveDecimalId(value: unknown, message: string): string | null {
  if (value === null || value === undefined) return null
  return parsePositiveDecimalId(value, message)
}

function parseCanonicalIdList(value: unknown, message: string): string[] {
  if (!Array.isArray(value)) throw new Error(message)
  const ids = value.map(entry => parsePositiveDecimalId(entry, message))
  for (let index = 1; index < ids.length; index += 1) if (BigInt(ids[index - 1]) >= BigInt(ids[index])) throw new Error(message)
  return ids
}

function parseCoordinate(value: unknown, message: string): { x: number; y: number } {
  if (!isRecord(value)) throw new Error(message)
  return {
    x: parseRequiredNonNegativeInteger(value.x, message),
    y: parseRequiredNonNegativeInteger(value.y, message),
  }
}

function parseWorkActivity(value: unknown): WorkActivity {
  if (!isRecord(value)) throw new Error('The server returned invalid citizen work activity.')
  return {
    foragingMinutes: parseRequiredNonNegativeInteger(value.foragingMinutes, 'The server returned invalid citizen work activity.'),
    woodcuttingMinutes: parseRequiredNonNegativeInteger(value.woodcuttingMinutes, 'The server returned invalid citizen work activity.'),
    stoneworkingMinutes: parseRequiredNonNegativeInteger(value.stoneworkingMinutes, 'The server returned invalid citizen work activity.'),
    constructionMinutes: parseRequiredNonNegativeInteger(value.constructionMinutes, 'The server returned invalid citizen work activity.'),
    haulingMinutes: parseRequiredNonNegativeInteger(value.haulingMinutes, 'The server returned invalid citizen work activity.'),
  }
}

function emptyWorkActivity(): WorkActivity {
  return { foragingMinutes: 0, woodcuttingMinutes: 0, stoneworkingMinutes: 0, constructionMinutes: 0, haulingMinutes: 0 }
}

function parseMovementPlan(value: unknown, actionSequence: number, location: { x: number; y: number }, target: { x: number; y: number } | null): CitizenMovementPlan | null {
  if (value === undefined || value === null) return null
  if (!isRecord(value) || !Array.isArray(value.waypoints)) throw new Error('The server returned an invalid citizen movement plan.')
  const planSequence = parseRequiredNonNegativeInteger(value.actionSequence, 'The server returned an invalid citizen movement sequence.')
  const observedMinute = parseRequiredNonNegativeInteger(value.observedMinute, 'The server returned an invalid citizen movement observation minute.')
  if (planSequence !== actionSequence || value.waypoints.length < 2) throw new Error('The server returned an incoherent citizen movement plan.')
  const waypoints: CitizenMovementWaypoint[] = []
  for (const [index, entry] of value.waypoints.entries()) {
    if (!isRecord(entry)) throw new Error('The server returned an invalid citizen movement waypoint.')
    const point = parseCoordinate(entry, 'The server returned an invalid citizen movement waypoint.')
    const arriveMinute = parseRequiredNonNegativeInteger(entry.arriveMinute, 'The server returned an invalid citizen movement arrival minute.')
    if (index === 0 && (point.x !== location.x || point.y !== location.y || arriveMinute !== observedMinute)) throw new Error('The server returned an incoherent citizen movement origin.')
    if (index > 0) {
      const previous = waypoints[index - 1]
      const dx = Math.abs(point.x - previous.x)
      const dy = Math.abs(point.y - previous.y)
      if (arriveMinute <= previous.arriveMinute || dx > 1 || dy > 1 || dx + dy === 0) throw new Error('The server returned an incoherent citizen movement route.')
    }
    waypoints.push({ ...point, arriveMinute })
  }
  const destination = waypoints.at(-1)!
  if (target === null || destination.x !== target.x || destination.y !== target.y) throw new Error('The server returned an incoherent citizen movement destination.')
  const segmentStartedMinute = value.segmentStartedMinute === undefined || value.segmentStartedMinute === null ? undefined : parseRequiredNonNegativeInteger(value.segmentStartedMinute, 'The server returned an invalid movement departure minute.')
  if (segmentStartedMinute !== undefined && (segmentStartedMinute > observedMinute || segmentStartedMinute >= waypoints[1].arriveMinute)) throw new Error('The server returned an incoherent movement departure minute.')
  return { actionSequence: planSequence, observedMinute, ...(segmentStartedMinute === undefined ? {} : { segmentStartedMinute }), waypoints }
}

export function parseCitizens(value: unknown): Citizen[] {
  // Older M0/M1 test doubles and servers do not expose citizens yet.
  if (!Array.isArray(value)) return []
  const result: Citizen[] = []
  for (const item of value) {
    if (!isRecord(item) || typeof item.name !== 'string' || !item.name.trim() || typeof item.age !== 'number' || !Number.isSafeInteger(item.age) || item.age < 0 || typeof item.lifeStage !== 'string' || !isRecord(item.location) || typeof item.location.x !== 'number' || !Number.isSafeInteger(item.location.x) || item.location.x < 0 || typeof item.location.y !== 'number' || !Number.isSafeInteger(item.location.y) || item.location.y < 0 || typeof item.health !== 'number' || !Number.isSafeInteger(item.health) || item.health < 0 || item.health > 10000 || typeof item.currentAction !== 'string' || !ACTIONS.includes(item.currentAction as typeof ACTIONS[number]) || typeof item.actionSequence !== 'number' || !Number.isSafeInteger(item.actionSequence) || item.actionSequence < 0) throw new Error('The server returned an invalid citizen.')
    const citizenId = parsePositiveDecimalId(item.citizenId, 'The server returned an invalid citizen.')
    const actionStartedMinute = parseActionMinute(item.actionStartedMinute, 'The server returned an invalid citizen action start minute.')
    const actionCompletesMinute = parseActionMinute(item.actionCompletesMinute, 'The server returned an invalid citizen action completion minute.')
    if (actionStartedMinute !== null && actionCompletesMinute !== null && actionStartedMinute > actionCompletesMinute) throw new Error('The server returned incoherent citizen action timing.')
    const target = item.target === undefined || item.target === null ? null : parseCoordinate(item.target, 'The server returned an invalid citizen action target.')
    const movementPlan = parseMovementPlan(item.movementPlan, item.actionSequence, item.location as { x: number; y: number }, target)
    const actionPhase = item.actionPhase === undefined ? 'None' : item.actionPhase
    if (typeof actionPhase !== 'string' || !ACTION_PHASES.includes(actionPhase as typeof ACTION_PHASES[number])) throw new Error('The server returned an invalid citizen action phase.')
    const isAlive = item.isAlive === undefined ? true : item.isAlive
    if (typeof isAlive !== 'boolean') throw new Error('The server returned an invalid citizen alive state.')
    const hunger = item.hunger === undefined ? null : parseNullableNonNegativeInteger(item.hunger, 'The server returned an invalid citizen hunger.')
    const rest = item.rest === undefined ? null : parseNullableNonNegativeInteger(item.rest, 'The server returned an invalid citizen rest.')
    const shelter = item.shelter === undefined ? null : parseNullableNonNegativeInteger(item.shelter, 'The server returned an invalid citizen shelter need.')
    const social = item.social === undefined ? null : parseNullableNonNegativeInteger(item.social, 'The server returned an invalid citizen social need.')
    if ([hunger, rest, shelter, social].some(need => need !== null && need > 10000)) throw new Error('The server returned an invalid citizen need.')
    const deathMinute = item.deathMinute === undefined ? null : parseNullableNonNegativeInteger(item.deathMinute, 'The server returned an invalid citizen death minute.')
    const deathCause = item.deathCause === undefined ? null : parseNullableString(item.deathCause, 'The server returned an invalid citizen death cause.')
    const carriedResource = item.carriedResource === undefined ? null : parseNullableResource(item.carriedResource)
    const carriedQuantity = item.carriedQuantity === undefined ? null : parseNullableNonNegativeInteger(item.carriedQuantity, 'The server returned an invalid carried quantity.')
    const targetResourceNodeId = item.targetResourceNodeId === undefined || item.targetResourceNodeId === null ? null : parsePositiveDecimalId(item.targetResourceNodeId, 'The server returned an invalid resource node ID.')
    const homeStructureId = parseNullablePositiveDecimalId(item.homeStructureId, 'The server returned an invalid home structure ID.')
    const targetStructureId = parseNullablePositiveDecimalId(item.targetStructureId, 'The server returned an invalid target structure ID.')
    const occupation = item.occupation === undefined ? 'Generalist' : item.occupation
    if (typeof occupation !== 'string' || !OCCUPATIONS.includes(occupation as CitizenOccupation)) throw new Error('The server returned an invalid citizen occupation.')
    const lifetimeWorkActivity = item.lifetimeWorkActivity === undefined ? emptyWorkActivity() : parseWorkActivity(item.lifetimeWorkActivity)
    const founderOrdinal = item.founderOrdinal === undefined || item.founderOrdinal === null ? null : parseRequiredNonNegativeInteger(item.founderOrdinal, 'The server returned an invalid founder ordinal.')
    if (founderOrdinal !== null && founderOrdinal > 19) throw new Error('The server returned an invalid founder ordinal.')
    const parentAId = parseNullablePositiveDecimalId(item.parentAId, 'The server returned an invalid parent ID.')
    const parentBId = parseNullablePositiveDecimalId(item.parentBId, 'The server returned an invalid parent ID.')
    if (parentAId !== null && parentAId === parentBId) throw new Error('The server returned duplicate parent IDs.')
    const partnerId = parseNullablePositiveDecimalId(item.partnerId, 'The server returned an invalid partner ID.')
    const householdId = parseNullablePositiveDecimalId(item.householdId, 'The server returned an invalid household ID.')
    const childrenIds = item.childrenIds === undefined ? [] : parseCanonicalIdList(item.childrenIds, 'The server returned invalid children IDs.')
    const targetCitizenId = parseNullablePositiveDecimalId(item.targetCitizenId, 'The server returned an invalid target citizen ID.')
    result.push({ ...item, citizenId, actionStartedMinute, actionCompletesMinute, target, actionPhase, isAlive, deathMinute, deathCause, hunger, rest, shelter, social, carriedResource, carriedQuantity, targetResourceNodeId, homeStructureId, targetStructureId, occupation: occupation as CitizenOccupation, lifetimeWorkActivity, founderOrdinal, parentAId, parentBId, partnerId, householdId, childrenIds, targetCitizenId, movementPlan } as unknown as Citizen)
  }
  return result
}
export async function fetchCitizens(): Promise<Citizen[]> { return parseCitizens(await get('/api/v1/citizens')) }

export function parseRelationships(value: unknown): Relationship[] {
  if (!Array.isArray(value)) throw new Error('The server returned an invalid relationships list.')
  const relationships = value.map(item => {
    if (!isRecord(item) || typeof item.otherCitizenName !== 'string' || !item.otherCitizenName.trim() || typeof item.label !== 'string' || !RELATIONSHIP_LABELS.includes(item.label as RelationshipLabel)) throw new Error('The server returned an invalid relationship.')
    const familiarity = parseRequiredNonNegativeInteger(item.familiarity, 'The server returned an invalid relationship familiarity.')
    const affinity = typeof item.affinity === 'number' && Number.isSafeInteger(item.affinity) && item.affinity >= -10000 && item.affinity <= 10000 ? item.affinity : null
    const trust = parseRequiredNonNegativeInteger(item.trust, 'The server returned an invalid relationship trust.')
    const conflict = parseRequiredNonNegativeInteger(item.conflict, 'The server returned an invalid relationship conflict.')
    const interactionCount = parseRequiredNonNegativeInteger(item.interactionCount, 'The server returned an invalid relationship interaction count.')
    if (familiarity > 10000 || trust > 10000 || conflict > 10000 || interactionCount === 0 || affinity === null) throw new Error('The server returned an invalid relationship.')
    return { otherCitizenId: parsePositiveDecimalId(item.otherCitizenId, 'The server returned an invalid relationship citizen ID.'), otherCitizenName: item.otherCitizenName, familiarity, affinity, trust, conflict, lastInteractionMinute: parseRequiredNonNegativeInteger(item.lastInteractionMinute, 'The server returned an invalid relationship interaction minute.'), interactionCount, label: item.label as RelationshipLabel }
  })
  for (let index = 1; index < relationships.length; index += 1) if (BigInt(relationships[index - 1].otherCitizenId) >= BigInt(relationships[index].otherCitizenId)) throw new Error('The server returned relationships out of canonical order.')
  return relationships
}

export async function fetchRelationships(citizenId: string): Promise<Relationship[]> {
  return parseRelationships(await get(`/api/v1/citizens/${encodeURIComponent(parsePositiveDecimalId(citizenId, 'Invalid citizen ID.'))}/relationships`))
}

export function parseHousehold(value: unknown): Household {
  if (!isRecord(value)) throw new Error('The server returned an invalid household.')
  const memberIds = parseCanonicalIdList(value.memberIds, 'The server returned invalid household members.')
  const livingMemberIds = parseCanonicalIdList(value.livingMemberIds, 'The server returned invalid living household members.')
  const childrenIds = parseCanonicalIdList(value.childrenIds, 'The server returned invalid household children.')
  if (livingMemberIds.some(id => !memberIds.includes(id)) || childrenIds.some(id => !memberIds.includes(id))) throw new Error('The server returned incoherent household members.')
  const partnerPair = value.partnerPair === null || value.partnerPair === undefined ? null : parseCanonicalIdList(value.partnerPair, 'The server returned an invalid household partnership.')
  if (partnerPair !== null && (partnerPair.length !== 2 || partnerPair.some(id => !memberIds.includes(id)))) throw new Error('The server returned an invalid household partnership.')
  const createdMinute = parseRequiredNonNegativeInteger(value.createdMinute, 'The server returned an invalid household creation minute.')
  const dissolvedMinute = parseNullableNonNegativeInteger(value.dissolvedMinute, 'The server returned an invalid household dissolved minute.')
  if (dissolvedMinute !== null && dissolvedMinute < createdMinute) throw new Error('The server returned an incoherent household timeline.')
  return { householdId: parsePositiveDecimalId(value.householdId, 'The server returned an invalid household ID.'), createdMinute, dissolvedMinute, dwellingStructureId: parseNullablePositiveDecimalId(value.dwellingStructureId, 'The server returned an invalid household dwelling.'), memberIds, livingMemberIds, partnerPair, childrenIds }
}

export function parseHouseholds(value: unknown): Household[] {
  if (!Array.isArray(value)) throw new Error('The server returned an invalid households list.')
  const households = value.map(parseHousehold)
  for (let index = 1; index < households.length; index += 1) if (BigInt(households[index - 1].householdId) >= BigInt(households[index].householdId)) throw new Error('The server returned households out of canonical order.')
  return households
}

export async function fetchHouseholds(): Promise<Household[]> { return parseHouseholds(await get('/api/v1/households')) }
export async function fetchHousehold(householdId: string): Promise<Household> { return parseHousehold(await get(`/api/v1/households/${encodeURIComponent(parsePositiveDecimalId(householdId, 'Invalid household ID.'))}`)) }

function parseStructureContribution(value: unknown): StructureContribution {
  if (!isRecord(value)) throw new Error('The server returned an invalid structure contribution.')
  return {
    citizenId: parsePositiveDecimalId(value.citizenId, 'The server returned an invalid contribution citizen ID.'),
    constructionWork: parseRequiredNonNegativeInteger(value.constructionWork, 'The server returned an invalid structure contribution.'),
    woodDelivered: parseRequiredNonNegativeInteger(value.woodDelivered, 'The server returned an invalid structure contribution.'),
    stoneDelivered: parseRequiredNonNegativeInteger(value.stoneDelivered, 'The server returned an invalid structure contribution.'),
  }
}

function parseNullablePositiveInteger(value: unknown, message: string): number | null {
  if (value === null || value === undefined) return null
  const parsed = parseRequiredNonNegativeInteger(value, message)
  if (parsed === 0) throw new Error(message)
  return parsed
}

export function parseStructure(value: unknown): Structure {
  if (!isRecord(value)) throw new Error('The server returned an invalid structure.')
  if (typeof value.type !== 'string' || !STRUCTURE_TYPES.includes(value.type as StructureType)) throw new Error('The server returned an invalid structure type.')
  if (typeof value.status !== 'string' || !STRUCTURE_STATUSES.includes(value.status as StructureStatus)) throw new Error('The server returned an invalid structure status.')
  if (value.cropStage !== undefined && value.cropStage !== null && (typeof value.cropStage !== 'string' || !['Fallow', 'Planted', 'Growing', 'Harvest', 'Dormant'].includes(value.cropStage))) throw new Error('The server returned an invalid crop stage.')
  const requiredWood = parseRequiredNonNegativeInteger(value.requiredWood, 'The server returned invalid structure materials.')
  const deliveredWood = parseRequiredNonNegativeInteger(value.deliveredWood, 'The server returned invalid structure materials.')
  const requiredStone = parseRequiredNonNegativeInteger(value.requiredStone, 'The server returned invalid structure materials.')
  const deliveredStone = parseRequiredNonNegativeInteger(value.deliveredStone, 'The server returned invalid structure materials.')
  const requiredWork = parseNullablePositiveInteger(value.requiredWork, 'The server returned invalid structure work.')
  if (requiredWork === null) throw new Error('The server returned invalid structure work.')
  const completedWork = parseRequiredNonNegativeInteger(value.completedWork, 'The server returned invalid structure work.')
  const completedMinute = parseNullableNonNegativeInteger(value.completedMinute, 'The server returned an invalid structure completion minute.')
  const contributions = value.contributions === undefined ? [] : value.contributions
  const currentOccupantIds = value.currentOccupantIds === undefined ? [] : value.currentOccupantIds
  if (!Array.isArray(contributions) || !Array.isArray(currentOccupantIds)) throw new Error('The server returned invalid structure lists.')
  const parsedContributions = contributions.map(parseStructureContribution)
  const occupants = currentOccupantIds.map(entry => parsePositiveDecimalId(entry, 'The server returned an invalid structure occupant ID.'))
  if (deliveredWood > requiredWood || deliveredStone > requiredStone || completedWork > requiredWork || value.condition !== 0 && value.condition !== 10000 || (value.status === 'UnderConstruction' && completedMinute !== null) || (value.status === 'Complete' && (completedMinute === null || completedWork !== requiredWork))) throw new Error('The server returned incoherent structure progress.')
  if (new Set(parsedContributions.map(entry => entry.citizenId)).size !== parsedContributions.length || new Set(occupants).size !== occupants.length) throw new Error('The server returned duplicate structure references.')
  return {
    structureId: parsePositiveDecimalId(value.structureId, 'The server returned an invalid structure ID.'), type: value.type as StructureType, cropStage: value.cropStage as string | null | undefined, status: value.status as StructureStatus,
    location: parseCoordinate(value.location, 'The server returned an invalid structure location.'), startedMinute: parseRequiredNonNegativeInteger(value.startedMinute, 'The server returned an invalid structure start minute.'), completedMinute,
    requiredWood, deliveredWood, requiredStone, deliveredStone, requiredWork, completedWork, condition: parseRequiredNonNegativeInteger(value.condition, 'The server returned an invalid structure condition.'),
    capacity: parseNullablePositiveInteger(value.capacity, 'The server returned an invalid structure capacity.'), storageBonus: parseNullablePositiveInteger(value.storageBonus, 'The server returned an invalid structure storage bonus.'), constructionMultiplierBasisPoints: parseNullablePositiveInteger(value.constructionMultiplierBasisPoints, 'The server returned an invalid construction multiplier.'), currentOccupantIds: occupants, contributions: parsedContributions,
  }
}

export function parseStructures(value: unknown): Structure[] {
  if (!Array.isArray(value)) throw new Error('The server returned an invalid structures list.')
  const structures = value.map(parseStructure)
  for (let index = 1; index < structures.length; index += 1) if (BigInt(structures[index - 1].structureId) >= BigInt(structures[index].structureId)) throw new Error('The server returned structures out of canonical order.')
  return structures
}

export async function fetchStructures(): Promise<Structure[]> { return parseStructures(await get('/api/v1/structures')) }

export function parseMap(value: unknown): Map {
  if (!isRecord(value)) throw new Error('The server returned an invalid map.')
  const width = parseNullablePositiveInteger(value.width, 'The server returned an invalid map width.')
  const height = parseNullablePositiveInteger(value.height, 'The server returned an invalid map height.')
  if (width === null || height === null || !Array.isArray(value.terrain) || width * height > Number.MAX_SAFE_INTEGER || value.terrain.length !== width * height) throw new Error('The server returned an invalid row-major terrain map.')
  const terrain = value.terrain.map(entry => {
    const terrainType = parseRequiredNonNegativeInteger(entry, 'The server returned an invalid terrain type.')
    if (terrainType < 1 || terrainType > 5) throw new Error('The server returned an invalid terrain type.')
    return terrainType
  })
  const elevationValue = value.elevation === undefined ? Array.from({ length: width * height }, () => 0) : value.elevation
  if (!Array.isArray(elevationValue) || elevationValue.length !== width * height) throw new Error('The server returned an invalid row-major elevation map.')
  const elevation = elevationValue.map(entry => {
    const normalized = parseRequiredNonNegativeInteger(entry, 'The server returned an invalid elevation value.')
    if (normalized > 10000) throw new Error('The server returned an invalid elevation value.')
    return normalized
  })
  const startingSite = parseCoordinate(value.startingSite, 'The server returned an invalid starting site.')
  if (startingSite.x >= width || startingSite.y >= height) throw new Error('The server returned an out-of-bounds starting site.')
  const resourcesValue = value.resources === undefined ? [] : value.resources
  if (!Array.isArray(resourcesValue)) throw new Error('The server returned invalid map resources.')
  const resources = resourcesValue.map(entry => {
    if (!isRecord(entry)) throw new Error('The server returned an invalid map resource.')
    const resourceType = parseNullableResource(entry.resourceType)
    const location = parseCoordinate(entry.location, 'The server returned an invalid map resource location.')
    const maximumQuantity = parseRequiredNonNegativeInteger(entry.maximumQuantity, 'The server returned an invalid map resource maximum quantity.')
    const regenerationPotential = parseRequiredNonNegativeInteger(entry.regenerationPotential, 'The server returned an invalid map resource regeneration potential.')
    if (resourceType === null || location.x >= width || location.y >= height || maximumQuantity === 0 || regenerationPotential > 10000) throw new Error('The server returned an invalid map resource.')
    return { resourceNodeId: parsePositiveDecimalId(entry.resourceNodeId, 'The server returned an invalid map resource ID.'), resourceType, location, maximumQuantity, regenerationPotential }
  })
  for (let index = 1; index < resources.length; index += 1) if (BigInt(resources[index - 1].resourceNodeId) >= BigInt(resources[index].resourceNodeId)) throw new Error('The server returned map resources out of canonical order.')
  return { width, height, terrain, elevation, resources, startingSite }
}

export async function fetchMap(): Promise<Map> { return parseMap(await get('/api/v1/map')) }

function parseSettlementResourceQuantity(value: unknown): SettlementResourceQuantity {
  if (!isRecord(value)) throw new Error('The server returned an invalid settlement resource quantity.')
  const resourceType = parseNullableResource(value.resourceType)
  if (resourceType === null) throw new Error('The server returned an invalid settlement resource type.')
  return { resourceType, quantity: parseRequiredNonNegativeInteger(value.quantity, 'The server returned an invalid settlement resource quantity.') }
}

function parseSettlementResource(value: unknown): SettlementResource {
  if (!isRecord(value)) throw new Error('The server returned an invalid settlement resource.')
  const resourceType = parseNullableResource(value.resourceType)
  if (resourceType === null) throw new Error('The server returned an invalid settlement resource type.')
  return {
    resourceNodeId: parsePositiveDecimalId(value.resourceNodeId, 'The server returned an invalid settlement resource node ID.'),
    resourceType,
    currentQuantity: parseRequiredNonNegativeInteger(value.currentQuantity, 'The server returned an invalid settlement resource quantity.'),
  }
}

export function parseSettlement(value: unknown): Settlement {
  if (!isRecord(value)) throw new Error('The server returned an invalid settlement.')
  const livingPopulation = parseRequiredNonNegativeInteger(value.livingPopulation, 'The server returned an invalid living population.')
  const deadPopulation = parseRequiredNonNegativeInteger(value.deadPopulation, 'The server returned an invalid dead population.')
  const totalPopulation = value.totalPopulation === undefined ? livingPopulation + deadPopulation : parseRequiredNonNegativeInteger(value.totalPopulation, 'The server returned an invalid total population.')
  if (totalPopulation !== livingPopulation + deadPopulation) throw new Error('The server returned incoherent population totals.')
  const remainingResources = value.remainingResources === undefined ? [] : value.remainingResources
  const resources = value.resources === undefined ? [] : value.resources
  if (!Array.isArray(remainingResources) || !Array.isArray(resources)) throw new Error('The server returned invalid settlement resources.')
  const storageCapacity = value.storageCapacity === undefined ? 0 : parseRequiredNonNegativeInteger(value.storageCapacity, 'The server returned an invalid storage capacity.')
  const storageUsed = value.storageUsed === undefined ? 0 : parseRequiredNonNegativeInteger(value.storageUsed, 'The server returned an invalid storage use.')
  const shelterCapacity = value.shelterCapacity === undefined ? 0 : parseRequiredNonNegativeInteger(value.shelterCapacity, 'The server returned an invalid shelter capacity.')
  const shelteredPopulation = value.shelteredPopulation === undefined ? 0 : parseRequiredNonNegativeInteger(value.shelteredPopulation, 'The server returned an invalid sheltered population.')
  const unhousedPopulation = value.unhousedPopulation === undefined ? livingPopulation : parseRequiredNonNegativeInteger(value.unhousedPopulation, 'The server returned an invalid unhoused population.')
  const completedShelters = value.completedShelters === undefined ? 0 : parseRequiredNonNegativeInteger(value.completedShelters, 'The server returned an invalid completed shelter count.')
  const completedStockpiles = value.completedStockpiles === undefined ? 0 : parseRequiredNonNegativeInteger(value.completedStockpiles, 'The server returned an invalid completed stockpile count.')
  const completedWorkshops = value.completedWorkshops === undefined ? 0 : parseRequiredNonNegativeInteger(value.completedWorkshops, 'The server returned an invalid completed workshop count.')
  const completedFarms = value.completedFarms === undefined ? 0 : parseRequiredNonNegativeInteger(value.completedFarms, 'Invalid farm count.')
  const completedGranaries = value.completedGranaries === undefined ? 0 : parseRequiredNonNegativeInteger(value.completedGranaries, 'Invalid granary count.')
  const completedMarketplaces = value.completedMarketplaces === undefined ? 0 : parseRequiredNonNegativeInteger(value.completedMarketplaces, 'Invalid marketplace count.')
  const exposureGraceUntilMinute = value.exposureGraceUntilMinute === undefined ? 0 : parseRequiredNonNegativeInteger(value.exposureGraceUntilMinute, 'The server returned an invalid exposure grace minute.')
  const householdCount = value.householdCount === undefined ? 0 : parseRequiredNonNegativeInteger(value.householdCount, 'The server returned an invalid household count.')
  const activeHouseholdCount = value.activeHouseholdCount === undefined ? 0 : parseRequiredNonNegativeInteger(value.activeHouseholdCount, 'The server returned an invalid active household count.')
  const partnershipCount = value.partnershipCount === undefined ? 0 : parseRequiredNonNegativeInteger(value.partnershipCount, 'The server returned an invalid partnership count.')
  const relationshipCount = value.relationshipCount === undefined ? 0 : parseRequiredNonNegativeInteger(value.relationshipCount, 'The server returned an invalid relationship count.')
  const friendCount = value.friendCount === undefined ? 0 : parseRequiredNonNegativeInteger(value.friendCount, 'The server returned an invalid friend count.')
  const rivalCount = value.rivalCount === undefined ? 0 : parseRequiredNonNegativeInteger(value.rivalCount, 'The server returned an invalid rival count.')
  const youngChildCount = value.youngChildCount === undefined ? 0 : parseRequiredNonNegativeInteger(value.youngChildCount, 'The server returned an invalid young-child count.')
  const childCount = value.childCount === undefined ? 0 : parseRequiredNonNegativeInteger(value.childCount, 'The server returned an invalid child count.')
  const adolescentCount = value.adolescentCount === undefined ? 0 : parseRequiredNonNegativeInteger(value.adolescentCount, 'The server returned an invalid adolescent count.')
  const adultCount = value.adultCount === undefined ? 0 : parseRequiredNonNegativeInteger(value.adultCount, 'The server returned an invalid adult count.')
  const elderCount = value.elderCount === undefined ? 0 : parseRequiredNonNegativeInteger(value.elderCount, 'The server returned an invalid elder count.')
  const activeConstructionProject = value.activeConstructionProject === undefined || value.activeConstructionProject === null ? null : parseStructure(value.activeConstructionProject)
  const hasLifeStageCounts = ['youngChildCount', 'childCount', 'adolescentCount', 'adultCount', 'elderCount'].some(field => field in value)
  if (storageUsed > storageCapacity || shelteredPopulation > shelterCapacity || shelteredPopulation + unhousedPopulation !== livingPopulation || activeHouseholdCount > householdCount || friendCount > relationshipCount || rivalCount > relationshipCount || (hasLifeStageCounts && youngChildCount + childCount + adolescentCount + adultCount + elderCount !== totalPopulation) || (activeConstructionProject !== null && activeConstructionProject.status !== 'UnderConstruction')) throw new Error('The server returned incoherent settlement construction data.')
  return {
    foodStored: parseRequiredNonNegativeInteger(value.foodStored, 'The server returned an invalid food stockpile.'),
    woodStored: parseRequiredNonNegativeInteger(value.woodStored, 'The server returned an invalid wood stockpile.'),
    stoneStored: parseRequiredNonNegativeInteger(value.stoneStored, 'The server returned an invalid stone stockpile.'),
    livingPopulation,
    deadPopulation,
    totalPopulation,
    remainingResources: remainingResources.map(parseSettlementResourceQuantity),
    resources: resources.map(parseSettlementResource),
    storageCapacity, storageUsed, shelterCapacity, shelteredPopulation, unhousedPopulation, completedShelters, completedStockpiles, completedWorkshops, completedFarms, completedGranaries, completedMarketplaces, exposureGraceUntilMinute, activeConstructionProject,
    householdCount, activeHouseholdCount, partnershipCount, relationshipCount, friendCount, rivalCount, youngChildCount, childCount, adolescentCount, adultCount, elderCount,
  }
}

export async function fetchSettlement(): Promise<Settlement> { return parseSettlement(await get('/api/v1/settlement')) }
