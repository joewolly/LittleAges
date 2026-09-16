export type Health = { ok: boolean; label: string }

export type Status = {
  state: string
  worldMinute: number | null
  pendingEventCount: number | null
  worldSeed: string | null
  error: string | null
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
}

export type CitizenOccupation = 'Generalist' | 'Forager' | 'Lumberjack' | 'Stoneworker' | 'Builder' | 'Hauler'
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

export type StructureType = 'Shelter' | 'Stockpile' | 'Workshop'
export type StructureStatus = 'UnderConstruction' | 'Complete'
export type StructureContribution = { citizenId: string; constructionWork: number; woodDelivered: number; stoneDelivered: number }
export type Structure = {
  structureId: string
  type: StructureType
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

export type Map = { width: number; height: number; terrain: number[]; startingSite: { x: number; y: number } }

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

export async function fetchHealth(): Promise<Health> { return parseHealth(await get('/api/v1/health')) }
export async function fetchStatus(): Promise<Status> { return parseStatus(await get('/api/v1/status')) }

const ACTIONS = ['None', 'Idle', 'Rest', 'Wander', 'Explore', 'Eat', 'GatherFood', 'GatherWood', 'GatherStone', 'Dead', 'HaulConstruction', 'Build', 'Socialize'] as const
const ACTION_PHASES = ['None', 'TravelToTarget', 'Perform', 'ReturnToStockpile', 'TravelToStockpile', 'TransportToConstruction', 'WaitingForStorage'] as const
const RESOURCE_TYPES = ['Food', 'Wood', 'Stone'] as const
const OCCUPATIONS = ['Generalist', 'Forager', 'Lumberjack', 'Stoneworker', 'Builder', 'Hauler'] as const
const STRUCTURE_TYPES = ['Shelter', 'Stockpile', 'Workshop'] as const
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

export function parseCitizens(value: unknown): Citizen[] {
  // Older M0/M1 test doubles and servers do not expose citizens yet.
  if (!Array.isArray(value)) return []
  const result: Citizen[] = []
  for (const item of value) {
    if (!isRecord(item) || typeof item.name !== 'string' || !item.name.trim() || typeof item.age !== 'number' || !Number.isSafeInteger(item.age) || item.age < 0 || typeof item.lifeStage !== 'string' || !isRecord(item.location) || typeof item.location.x !== 'number' || !Number.isSafeInteger(item.location.x) || item.location.x < 0 || typeof item.location.y !== 'number' || !Number.isSafeInteger(item.location.y) || item.location.y < 0 || typeof item.health !== 'number' || !Number.isSafeInteger(item.health) || item.health < 0 || item.health > 10000 || typeof item.currentAction !== 'string' || !ACTIONS.includes(item.currentAction as typeof ACTIONS[number]) || typeof item.actionSequence !== 'number' || !Number.isSafeInteger(item.actionSequence) || item.actionSequence < 0) throw new Error('The server returned an invalid citizen.')
    const citizenId = parsePositiveDecimalId(item.citizenId, 'The server returned an invalid citizen.')
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
    result.push({ ...item, citizenId, actionPhase, isAlive, deathMinute, deathCause, hunger, rest, shelter, social, carriedResource, carriedQuantity, targetResourceNodeId, homeStructureId, targetStructureId, occupation: occupation as CitizenOccupation, lifetimeWorkActivity, founderOrdinal, parentAId, parentBId, partnerId, householdId, childrenIds, targetCitizenId } as unknown as Citizen)
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
    structureId: parsePositiveDecimalId(value.structureId, 'The server returned an invalid structure ID.'), type: value.type as StructureType, status: value.status as StructureStatus,
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
  const startingSite = parseCoordinate(value.startingSite, 'The server returned an invalid starting site.')
  if (startingSite.x >= width || startingSite.y >= height) throw new Error('The server returned an out-of-bounds starting site.')
  return { width, height, terrain, startingSite }
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
    storageCapacity, storageUsed, shelterCapacity, shelteredPopulation, unhousedPopulation, completedShelters, completedStockpiles, completedWorkshops, exposureGraceUntilMinute, activeConstructionProject,
    householdCount, activeHouseholdCount, partnershipCount, relationshipCount, friendCount, rivalCount, youngChildCount, childCount, adolescentCount, adultCount, elderCount,
  }
}

export async function fetchSettlement(): Promise<Settlement> { return parseSettlement(await get('/api/v1/settlement')) }
