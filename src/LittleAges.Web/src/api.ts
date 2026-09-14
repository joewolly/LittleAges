export type Health = { ok: boolean; label: string }

export type Status = {
  state: string
  worldMinute: number | null
  pendingEventCount: number | null
  worldSeed: string | null
  error: string | null
}

export type Citizen = {
  citizenId: string
  name: string
  age: number
  lifeStage: string
  location: { x: number; y: number }
  health: number
  currentAction: string
  actionSequence: number
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
  return {
    state: typeof data.state === 'string' ? data.state : 'Unknown',
    worldMinute: typeof data.worldMinute === 'number' && Number.isFinite(data.worldMinute) ? data.worldMinute : null,
    pendingEventCount: typeof data.pendingEventCount === 'number' && Number.isFinite(data.pendingEventCount) ? data.pendingEventCount : null,
    worldSeed: typeof data.worldSeed === 'string' ? data.worldSeed : null,
    error: typeof data.error === 'string' ? data.error : null,
  }
}

async function get(path: string): Promise<unknown> {
  const response = await fetch(path)
  if (!response.ok) throw new Error(`Request failed (${response.status})`)
  const text = await response.text()
  try { return JSON.parse(text) as unknown } catch { return text }
}

export async function fetchHealth(): Promise<Health> { return parseHealth(await get('/api/v1/health')) }
export async function fetchStatus(): Promise<Status> { return parseStatus(await get('/api/v1/status')) }
export function parseCitizens(value: unknown): Citizen[] {
  // Older M0/M1 test doubles and servers do not expose citizens yet.
  if (!Array.isArray(value)) return []
  const result: Citizen[] = []
  for (const item of value) {
    if (!isRecord(item) || typeof item.citizenId !== 'string' || !/^[1-9]\d*$/.test(item.citizenId) || typeof item.name !== 'string' || !item.name.trim() || typeof item.age !== 'number' || !Number.isInteger(item.age) || item.age < 0 || typeof item.lifeStage !== 'string' || !isRecord(item.location) || typeof item.location.x !== 'number' || !Number.isInteger(item.location.x) || item.location.x < 0 || typeof item.location.y !== 'number' || !Number.isInteger(item.location.y) || item.location.y < 0 || typeof item.health !== 'number' || !Number.isInteger(item.health) || item.health < 0 || item.health > 10000 || typeof item.currentAction !== 'string' || !['None', 'Idle', 'Rest', 'Wander', 'Explore'].includes(item.currentAction) || typeof item.actionSequence !== 'number' || !Number.isSafeInteger(item.actionSequence) || item.actionSequence < 0) throw new Error('The server returned an invalid citizen.')
    result.push(item as unknown as Citizen)
  }
  return result
}
export async function fetchCitizens(): Promise<Citizen[]> { return parseCitizens(await get('/api/v1/citizens')) }
