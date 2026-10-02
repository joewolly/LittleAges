import type { Citizen } from './api'

export const NEWCOMER_PHASES = ['Approaching', 'Visiting', 'Leaving', 'Resident', 'Departed', 'Dead'] as const
export type NewcomerPhase = typeof NEWCOMER_PHASES[number]
export type Newcomer = {
  origin: 'External'
  phase: NewcomerPhase
  hostSettlementId: string
  shelterStructureId: string
  entryTile: { x: number; y: number }
  firstSeenMinute: number
  visitingStartedMinute: number | null
  stayDeadlineMinute: number | null
  joinedMinute: number | null
  departedMinute: number | null
  deathMinute: number | null
  provisionsRemaining: number
}

export function isCitizenResident(citizen: Citizen): boolean { return !citizen.newcomer || citizen.newcomer.joinedMinute !== null }
export function isCitizenPresent(citizen: Citizen): boolean { return citizen.isAlive && citizen.newcomer?.phase !== 'Departed' }
export function isCitizenGuest(citizen: Citizen): boolean { return !isCitizenResident(citizen) && isCitizenPresent(citizen) }
export function citizenRoleLabel(citizen: Citizen): string { return isCitizenResident(citizen) ? citizen.occupation : 'External visitor' }
export function citizenObservationLabel(citizen: Citizen): string {
  const newcomer = citizen.newcomer
  if (newcomer?.phase === 'Departed') return 'Departed · last observed alive'
  if (!citizen.isAlive) return 'Dead'
  if (isCitizenGuest(citizen)) return `Visitor · ${newcomer!.phase.toLowerCase()}`
  return newcomer ? 'Resident · external newcomer' : 'Alive'
}

export function newcomerActivity(citizen: Citizen): string | null {
  if (!isCitizenGuest(citizen)) return citizen.newcomer?.phase === 'Departed' ? 'Last observed leaving the world' : null
  switch (citizen.newcomer!.phase) {
    case 'Approaching': return 'Approaching the settlement'
    case 'Leaving': return 'Leaving the settlement'
    default: return `Visiting · ${citizen.currentAction.toLowerCase()}`
  }
}
