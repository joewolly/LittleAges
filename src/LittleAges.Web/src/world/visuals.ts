import type { Citizen, CitizenMovementPlan, Map as WorldMap } from '../api'

export const MAX_TERRAIN_RELIEF = 3

export type ScenePoint = { x: number; y: number; z: number }
export type ContainedMapLayout = { scale: number; offsetX: number; offsetY: number; drawWidth: number; drawHeight: number }
export type WorldPoint = { x: number; y: number }

export function movementPlanIdentity(plan: CitizenMovementPlan | null): string {
  if (plan === null) return 'none'
  const destination = plan.waypoints[plan.waypoints.length - 1]
  return `${plan.actionSequence}:${destination.x},${destination.y}`
}

export function reconcileVisualMinute(currentMinute: number, currentIdentity: string, nextPlan: CitizenMovementPlan): number {
  return currentIdentity === movementPlanIdentity(nextPlan) ? Math.max(currentMinute, nextPlan.observedMinute) : nextPlan.observedMinute
}

export function stableVisualHash(...parts: Array<string | number>): number {
  let hash = 2166136261
  for (const part of parts) {
    const value = String(part)
    for (let index = 0; index < value.length; index += 1) {
      hash ^= value.charCodeAt(index)
      hash = Math.imul(hash, 16777619)
    }
    hash ^= 124
    hash = Math.imul(hash, 16777619)
  }
  return hash >>> 0
}

export function elevationAt(map: WorldMap, x: number, y: number): number {
  const clampedX = Math.max(0, Math.min(map.width - 1, Math.floor(x)))
  const clampedY = Math.max(0, Math.min(map.height - 1, Math.floor(y)))
  return map.elevation[clampedY * map.width + clampedX] / 10000 * MAX_TERRAIN_RELIEF
}

export function worldToScene(map: WorldMap, x: number, y: number, lift = 0): ScenePoint {
  return {
    x: x - (map.width - 1) / 2,
    y: elevationAt(map, x, y) + lift,
    z: y - (map.height - 1) / 2,
  }
}

export function containMap(width: number, height: number, mapWidth: number, mapHeight: number): ContainedMapLayout {
  const shortestSide = Math.max(1, Math.min(width, height))
  const padding = Math.min(20, shortestSide * 0.04)
  const scale = Math.max(0.01, Math.min((width - padding * 2) / mapWidth, (height - padding * 2) / mapHeight))
  const drawWidth = mapWidth * scale
  const drawHeight = mapHeight * scale
  return {
    scale,
    drawWidth,
    drawHeight,
    offsetX: (width - drawWidth) / 2,
    offsetY: (height - drawHeight) / 2,
  }
}

export function positionAlongMovementPlan(plan: CitizenMovementPlan, visualMinute: number): WorldPoint {
  const first = plan.waypoints[0]
  if (visualMinute <= first.arriveMinute) return { x: first.x, y: first.y }
  for (let index = 1; index < plan.waypoints.length; index += 1) {
    const next = plan.waypoints[index]
    if (visualMinute > next.arriveMinute) continue
    const previous = plan.waypoints[index - 1]
    const duration = next.arriveMinute - previous.arriveMinute
    const progress = duration <= 0 ? 1 : Math.max(0, Math.min(1, (visualMinute - previous.arriveMinute) / duration))
    return { x: previous.x + (next.x - previous.x) * progress, y: previous.y + (next.y - previous.y) * progress }
  }
  const last = plan.waypoints[plan.waypoints.length - 1]
  return { x: last.x, y: last.y }
}

export function scenePointAlongMovementPlan(map: WorldMap, plan: CitizenMovementPlan, visualMinute: number, lift = 0): ScenePoint {
  const first = plan.waypoints[0]
  if (visualMinute <= first.arriveMinute) return worldToScene(map, first.x, first.y, lift)
  for (let index = 1; index < plan.waypoints.length; index += 1) {
    const next = plan.waypoints[index]
    if (visualMinute > next.arriveMinute) continue
    const previous = plan.waypoints[index - 1]
    const duration = next.arriveMinute - previous.arriveMinute
    const progress = duration <= 0 ? 1 : Math.max(0, Math.min(1, (visualMinute - previous.arriveMinute) / duration))
    const from = worldToScene(map, previous.x, previous.y, lift)
    const to = worldToScene(map, next.x, next.y, lift)
    return { x: from.x + (to.x - from.x) * progress, y: from.y + (to.y - from.y) * progress, z: from.z + (to.z - from.z) * progress }
  }
  const last = plan.waypoints[plan.waypoints.length - 1]
  return worldToScene(map, last.x, last.y, lift)
}

export function shouldInterpolateCitizen(previous: Citizen, current: Citizen, operationalSpeed: number | null, reducedMotion: boolean): boolean {
  if (reducedMotion || operationalSpeed === null || operationalSpeed > 10 || previous.actionSequence !== current.actionSequence) return false
  const dx = current.location.x - previous.location.x
  const dy = current.location.y - previous.location.y
  return dx * dx + dy * dy <= 4
}

export function citizenPaletteIndex(worldSeed: string | null, citizenId: string): number {
  return stableVisualHash(worldSeed ?? 'unknown-world', 'citizen-palette', citizenId) % 8
}

export function detailVariant(worldSeed: string | null, domain: string, stableId: string | number): number {
  return stableVisualHash(worldSeed ?? 'unknown-world', domain, stableId)
}
