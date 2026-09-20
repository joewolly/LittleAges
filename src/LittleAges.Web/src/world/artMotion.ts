import type { AnimationMixer } from 'three'
import type { Citizen } from '../api'
import type { LivingOrder } from '../living'

export function citizenAnimation(citizen: Pick<Citizen, 'currentAction' | 'actionPhase' | 'carriedResource'>, work?: LivingOrder): string {
  const travelling = ['TravelToTarget', 'ReturnToStockpile', 'TravelToStockpile', 'TransportToConstruction'].includes(citizen.actionPhase)
  if (travelling) return citizen.carriedResource !== null || work && (work.cargoInTransit || work.phase === 'Travel' && !work.suppliesDelivered && work.ingredients.length > 0) ? 'Carry' : 'Walk'
  if (citizen.currentAction === 'LivingWork' && work) {
    if (['Care', 'Teach', 'Recreate', 'RepairRelationship'].includes(work.kind)) return 'Socialize'
    if (['Sow', 'Tend', 'Harvest', 'Hunt', 'EstablishField'].includes(work.kind)) return 'Gather'
    return 'Build'
  }
  if (citizen.currentAction === 'Rest') return 'Rest'
  if (citizen.currentAction === 'Socialize') return 'Socialize'
  if (citizen.currentAction === 'Build') return 'Build'
  if (citizen.carriedResource !== null) return 'Carry'
  if (citizen.currentAction === 'WorkFarm' || citizen.currentAction === 'HaulHarvest') return 'Gather'
  if (citizen.currentAction.startsWith('Gather') && citizen.actionPhase === 'Perform') return 'Gather'
  return 'Idle'
}

/** Imperative engine state, not React state: freezes clip time AND cross-fades. */
export function setAnimationPlayback(mixer: AnimationMixer, paused: boolean, reducedMotion: boolean, speed: number | null): void {
  mixer.timeScale = paused || reducedMotion || (speed ?? 0) > 10 ? 0 : 1
}
