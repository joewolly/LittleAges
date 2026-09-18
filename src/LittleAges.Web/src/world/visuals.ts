import type { Citizen, Map as WorldMap } from '../api'

export const MAX_TERRAIN_RELIEF = 3

export type ScenePoint = { x: number; y: number; z: number }

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
