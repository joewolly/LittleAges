import type { Citizen, CitizenMovementPlan, Structure } from '../api'
import { isCitizenGuest, isCitizenPresent, newcomerActivity } from '../newcomers'
import { festivalVisitorActivity, livingLabel, livingWorkStage, type LivingWorld } from '../living'
import { worldPointAlongMovementPlan } from './visuals'

export function shouldSnapToAuthority(paused: boolean, reducedMotion: boolean, operationalSpeed: number | null): boolean {
  return paused || reducedMotion || operationalSpeed === null || operationalSpeed <= 0 || operationalSpeed > 10
}

export type CitizenPose = { x: number; y: number; visible: boolean; walking: boolean; facingRight: boolean; actionSequence: number; observedMinute: number }

export function advanceCitizenPose(previous: CitizenPose | undefined, citizen: Citizen, structures: Structure[], observation: { worldMinute: number; visualMinute: number; speed: number | null; paused: boolean; reducedMotion: boolean }, delta: number): CitizenPose {
  const snap = shouldSnapToAuthority(observation.paused, observation.reducedMotion, observation.speed)
  const home = restingHome(citizen, structures)
  const plan = snap ? null : doorwayPlan(citizen, structures)
  const target = home ? doorway(home) : plan ? worldPointAlongMovementPlan(plan, observation.visualMinute, observation.speed) : citizen.location
  const base = { ...target, visible: home === null, walking: false, facingRight: previous?.facingRight ?? false, actionSequence: citizen.actionSequence, observedMinute: observation.worldMinute }
  // A new action or restored checkpoint must not blend with a stale route.
  if (!previous || snap || previous.actionSequence !== citizen.actionSequence || observation.worldMinute < previous.observedMinute) return base
  let from = previous
  if (!previous.visible && !home) {
    const house = structures.find(structure => structure.structureId === citizen.homeStructureId)
    from = { ...previous, ...(house ? doorway(house) : target), visible: true }
  }
  const dx = target.x - from.x, dy = target.y - from.y
  const distance = dx * dx + dy * dy
  const blend = 1 - Math.exp(-Math.min(delta, .1) * (plan ? 18 : 12))
  return { ...base, x: distance > .00001 ? from.x + dx * blend : target.x, y: distance > .00001 ? from.y + dy * blend : target.y,
    visible: home ? from.visible && distance >= .01 : true,
    walking: plan !== null && distance > .0004,
    facingRight: Math.abs(dx - dy) > .02 ? dx - dy > 0 : from.facingRight }
}

export function citizenActivity(citizen: Citizen, living: LivingWorld | null | undefined, structures: Structure[]): { label: string; cue: string } {
  const newcomer = newcomerActivity(citizen)
  if (newcomer) return { label: newcomer, cue: isCitizenPresent(citizen) ? 'V' : '' }
  if (!isCitizenPresent(citizen)) return { label: 'Dead', cue: '' }
  if (restingHome(citizen, structures)) return { label: 'Resting indoors', cue: 'Z' }
  if (citizen.currentAction === 'Rest') return { label: citizen.actionPhase === 'Perform' ? 'Resting' : 'Going to rest', cue: citizen.actionPhase === 'Perform' ? 'Z' : '→' }
  if (citizen.currentAction === 'Eat') return { label: citizen.actionPhase === 'Perform' ? 'Eating' : 'Going to eat', cue: citizen.actionPhase === 'Perform' ? 'E' : '→' }
  if (citizen.actionPhase === 'WaitingForStorage') return { label: `${livingLabel(citizen.currentAction)} · Blocked: waiting for storage`, cue: '!' }
  const festival = festivalVisitorActivity(living, citizen)
  if (festival) return { label: festival, cue: '•' }
  const order = living?.orders.find(order => order.citizenId === citizen.citizenId)
  if (order && citizen.currentAction === 'LivingWork') {
    const progress = `${order.workDone.toLocaleString()} / ${order.requiredWork.toLocaleString()}`
    if (order.blockedReason) return { label: `${livingLabel(order.kind)} · Blocked: ${livingLabel(order.blockedReason)} · ${progress}`, cue: '!' }
    if (order.workDone >= order.requiredWork) return { label: `${livingLabel(order.kind)} · Work complete · ${progress}`, cue: '✓' }
    const performing = order.phase === 'Work' && citizen.actionPhase === 'Perform'
    return { label: `${livingLabel(order.kind)} · ${performing ? order.kind === 'AttendFestival' ? 'Celebrating' : 'Work phase' : order.phase === 'Work' ? 'Waiting to work' : livingWorkStage(order)} · ${progress}`, cue: performing ? order.kind === 'AttendFestival' ? '•' : 'W' : citizen.movementPlan ? '→' : '…' }
  }
  if (citizen.movementPlan) return { label: `${livingLabel(citizen.currentAction)} · Traveling`, cue: '→' }
  if (citizen.currentAction === 'Idle' || citizen.currentAction === 'None') return { label: 'Idle · Between actions', cue: '…' }
  if (citizen.currentAction === 'Socialize') return { label: 'Socializing', cue: '•' }
  if (citizen.currentAction === 'Wander' || citizen.currentAction === 'Explore') return { label: livingLabel(citizen.currentAction), cue: '…' }
  return { label: `${livingLabel(citizen.currentAction)} · ${livingLabel(citizen.actionPhase)}`, cue: citizen.actionPhase === 'Perform' ? 'W' : '…' }
}

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
  if (!isCitizenPresent(citizen) || isCitizenGuest(citizen) || citizen.currentAction !== 'Rest' || citizen.actionPhase !== 'Perform') return null
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
