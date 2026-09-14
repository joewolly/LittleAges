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
  actionPhase: CitizenActionPhase
  carriedResource: ResourceType | null
  carriedQuantity: number | null
  targetResourceNodeId: string | null
}

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
}

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

const ACTIONS = ['None', 'Idle', 'Rest', 'Wander', 'Explore', 'Eat', 'GatherFood', 'GatherWood', 'GatherStone', 'Dead'] as const
const ACTION_PHASES = ['None', 'TravelToTarget', 'Perform', 'ReturnToStockpile'] as const
const RESOURCE_TYPES = ['Food', 'Wood', 'Stone'] as const

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
    if ((hunger !== null && hunger > 10000) || (rest !== null && rest > 10000)) throw new Error('The server returned an invalid citizen need.')
    const deathMinute = item.deathMinute === undefined ? null : parseNullableNonNegativeInteger(item.deathMinute, 'The server returned an invalid citizen death minute.')
    const deathCause = item.deathCause === undefined ? null : parseNullableString(item.deathCause, 'The server returned an invalid citizen death cause.')
    const carriedResource = item.carriedResource === undefined ? null : parseNullableResource(item.carriedResource)
    const carriedQuantity = item.carriedQuantity === undefined ? null : parseNullableNonNegativeInteger(item.carriedQuantity, 'The server returned an invalid carried quantity.')
    const targetResourceNodeId = item.targetResourceNodeId === undefined || item.targetResourceNodeId === null ? null : parsePositiveDecimalId(item.targetResourceNodeId, 'The server returned an invalid resource node ID.')
    result.push({ ...item, citizenId, actionPhase, isAlive, deathMinute, deathCause, hunger, rest, carriedResource, carriedQuantity, targetResourceNodeId } as unknown as Citizen)
  }
  return result
}
export async function fetchCitizens(): Promise<Citizen[]> { return parseCitizens(await get('/api/v1/citizens')) }

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
  return {
    foodStored: parseRequiredNonNegativeInteger(value.foodStored, 'The server returned an invalid food stockpile.'),
    woodStored: parseRequiredNonNegativeInteger(value.woodStored, 'The server returned an invalid wood stockpile.'),
    stoneStored: parseRequiredNonNegativeInteger(value.stoneStored, 'The server returned an invalid stone stockpile.'),
    livingPopulation,
    deadPopulation,
    totalPopulation,
    remainingResources: remainingResources.map(parseSettlementResourceQuantity),
    resources: resources.map(parseSettlementResource),
  }
}

export async function fetchSettlement(): Promise<Settlement> { return parseSettlement(await get('/api/v1/settlement')) }
