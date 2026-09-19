import type { Citizen, CitizenMovementPlan, Structure } from '../api'

/** Shared clock, bounded to one observation interval plus a small jitter allowance. */
export class PresentationClock {
  private minute = 0
  private receivedAt = 0
  private speed = 0
  private initialized = false
  observe(minute: number, speed: number | null, paused: boolean, now: number) {
    const current = this.at(now)
    // A restart can restore an older checkpoint; never predict across that boundary.
    this.minute = !this.initialized || minute < this.minute - 1 || paused ? minute : Math.min(minute + 0.1 * (speed ?? 0), Math.max(minute, current))
    this.receivedAt = now
    this.speed = paused ? 0 : speed ?? 0
    this.initialized = true
  }
  at(now: number) { return this.minute + Math.max(0, Math.min(Math.max(0.25, this.speed > 0 ? 1 / this.speed + 0.1 : 0), (now - this.receivedAt) / 1000)) * this.speed }
}

export function restingHome(citizen: Citizen, structures: Structure[]): Structure | null {
  if (!citizen.isAlive || citizen.currentAction !== 'Rest' || citizen.actionPhase !== 'Perform') return null
  return structures.find(s => s.structureId === citizen.homeStructureId && s.type === 'Shelter' && s.status === 'Complete' && s.location.x === citizen.location.x && s.location.y === citizen.location.y) ?? null
}

// GLTF exports Blender's -Y doorway on the positive scene Z face. The house is scaled by .9.
export function doorway(home: Structure) { return { x: home.location.x - 0.27 * 0.9, y: home.location.y + 1.2 * 0.9 } }

/** Map house endpoints to their doorway without changing the simulation's route or timing. */
export function doorwayPlan(citizen: Citizen, structures: Structure[]): CitizenMovementPlan | null {
  const plan = citizen.movementPlan
  if (!plan) return null
  const home = structures.find(s => s.structureId === citizen.homeStructureId && s.type === 'Shelter' && s.status === 'Complete')
  if (!home) return plan
  const atHome = (p: { x: number; y: number }) => p.x === home.location.x && p.y === home.location.y
  const fromHome = atHome(plan.waypoints[0])
  const toHome = citizen.currentAction === 'Rest' && atHome(plan.waypoints[plan.waypoints.length - 1])
  if (!fromHome && !toHome) return plan
  return { ...plan, waypoints: plan.waypoints.map((p, i) => (fromHome && i === 0) || (toHome && i === plan.waypoints.length - 1) ? { ...p, ...doorway(home) } : p) }
}

/** Reuse immutable scene objects when only a stream's time or citizen needs changed. */
export function retainStructures(previous: Structure[] | null, next: Structure[]): Structure[] {
  if (!previous) return next
  const byId = new Map(previous.map(item => [item.structureId, item]))
  const result = next.map(item => {
    const old = byId.get(item.structureId)
    return old && JSON.stringify(old) === JSON.stringify(item) ? old : item
  })
  return result.length === previous.length && result.every((item, index) => item === previous[index]) ? previous : result
}
