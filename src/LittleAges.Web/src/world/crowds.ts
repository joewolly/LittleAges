import type { Citizen } from '../api'
import { isCitizenPresent } from '../newcomers'
import type { WorldPoint } from './visuals'

export type CrowdPlacement = { offset: WorldPoint; memberIds: string[] }
const SLOTS: WorldPoint[] = [
  { x: 0, y: 0 }, { x: -.45, y: .45 }, { x: .45, y: -.45 },
  { x: -.45, y: -.45 }, { x: .45, y: .45 },
  { x: -.45, y: 0 }, { x: .45, y: 0 }, { x: 0, y: -.45 }, { x: 0, y: .45 },
]
const tileKey = (citizen: Citizen) => `${citizen.location.x},${citizen.location.y}`

/** Bounded cosmetic slots at actual tiles. Retain occupied slots through roster
 * reordering, arrivals and departures; crowded overflow remains in the picker. */
export class CrowdLayout {
  private slots = new Map<string, { tile: string; slot: number }>()
  update(citizens: Citizen[]): Map<string, CrowdPlacement> {
    const groups = new Map<string, Citizen[]>()
    for (const citizen of citizens.filter(isCitizenPresent)) {
      const key = tileKey(citizen)
      const group = groups.get(key) ?? []
      group.push(citizen)
      groups.set(key, group)
    }
    const nextSlots = new Map<string, { tile: string; slot: number }>()
    const placements = new Map<string, CrowdPlacement>()
    for (const [tile, group] of groups) {
      group.sort((a, b) => a.citizenId.length - b.citizenId.length || a.citizenId.localeCompare(b.citizenId))
      const used = new Set<number>()
      for (const citizen of group) {
        const old = this.slots.get(citizen.citizenId)
        if (old?.tile === tile) { nextSlots.set(citizen.citizenId, old); used.add(old.slot) }
      }
      const memberIds = group.map(citizen => citizen.citizenId)
      for (const citizen of group) {
        let assigned = nextSlots.get(citizen.citizenId)
        if (!assigned) {
          let slot = 0
          while (used.has(slot)) slot++
          assigned = { tile, slot }
          used.add(slot)
          nextSlots.set(citizen.citizenId, assigned)
        }
        placements.set(citizen.citizenId, { offset: SLOTS[assigned.slot % SLOTS.length], memberIds })
      }
    }
    this.slots = nextSlots
    return placements
  }
}

export function displayedPoint(point: WorldPoint, placement: CrowdPlacement | undefined): WorldPoint {
  return { x: point.x + (placement?.offset.x ?? 0), y: point.y + (placement?.offset.y ?? 0) }
}

/** Pick at the same points used for drawing and following. Expand overlapping
 * targets to their complete tile groups so even dense or indoor members select. */
export function citizenHitIds(point: WorldPoint, targets: Array<{ id: string; point: WorldPoint }>, radius: number, placements: Map<string, CrowdPlacement>): string[] {
  const hits = targets.map(target => ({ ...target, distance: Math.hypot(target.point.x - point.x, target.point.y - point.y) }))
    .filter(target => target.distance <= radius).sort((a, b) => a.distance - b.distance)
  return [...new Set(hits.flatMap(hit => placements.get(hit.id)?.memberIds ?? [hit.id]))]
}
