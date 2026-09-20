import type { AnimationMixer } from 'three'
import type { Citizen } from '../api'

export function citizenAnimation(citizen: Pick<Citizen, 'currentAction' | 'actionPhase' | 'carriedResource'>): string {
  const travelling = ['TravelToTarget', 'ReturnToStockpile', 'TravelToStockpile', 'TransportToConstruction'].includes(citizen.actionPhase)
  if (travelling) return citizen.carriedResource !== null ? 'Carry' : 'Walk'
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
