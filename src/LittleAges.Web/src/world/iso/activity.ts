import type { Citizen } from '../../api'
import type { LivingOrder } from '../../living'
import { isCitizenPresent } from '../../newcomers'
import { stableVisualHash } from '../visuals'

export type CitizenMotion = 'still' | 'idle' | 'rest' | 'eat' | 'leisure' | 'social' | 'celebrate' | 'work' | 'chop' | 'mine' | 'farm' | 'build' | 'experiment'
export type ActivityGesture = { kind: CitizenMotion; progress: number }

/** Only an observed Perform phase earns a work gesture; waiting never does. */
export function citizenMotion(citizen: Citizen, order: LivingOrder | undefined): CitizenMotion {
  if (!isCitizenPresent(citizen) || citizen.movementPlan || citizen.actionPhase !== 'Perform') return 'still'
  switch (citizen.currentAction) {
    case 'Idle': case 'None': return 'idle'
    case 'Rest': return 'rest'
    case 'Eat': return 'eat'
    case 'Socialize': return 'social'
    case 'GatherWood': return 'chop'
    case 'GatherStone': return 'mine'
    case 'GatherFood': return 'work'
    case 'WorkFarm': return 'farm'
    case 'Build': return 'build'
    case 'LivingWork':
      if (!order || order.citizenId !== citizen.citizenId || order.phase !== 'Work' || order.blockedReason || order.workDone >= order.requiredWork) return 'still'
      switch (order.kind) {
        case 'Recreate': return 'leisure'
        case 'AttendFestival': return 'celebrate'
        case 'Teach': case 'RepairRelationship': return 'social'
        case 'Experiment': return 'experiment'
        case 'CutFuel': return 'chop'
        case 'Sow': case 'Tend': case 'Harvest': return 'farm'
        case 'BuildFacility': case 'BuildRoad': return 'build'
        default: return 'work'
      }
    default: return 'still'
  }
}

/** Cosmetic motion changes the drawn sprite only, never its ground position. */
export function activityPose(kind: CitizenMotion, citizenId: string, now: number, walking: boolean, animated: boolean): { frame: 0 | 1; lift: number; lean: number; gesture?: ActivityGesture } {
  if (!animated) return { frame: 0, lift: 0, lean: 0 }
  const time = now + stableVisualHash(citizenId) % 3000
  if (walking) return { frame: (Math.floor(time / 250) % 2) as 0 | 1, lift: Math.abs(Math.sin(time / 250 * Math.PI)) * .025, lean: 0 }
  if (kind === 'still') return { frame: 0, lift: 0, lean: 0 }
  const quiet = kind === 'idle' || kind === 'rest'
  const relaxed = quiet || kind === 'leisure' || kind === 'social'
  const progress = Math.sin(time / (quiet ? 1400 : relaxed ? 650 : 350))
  return {
    frame: 0,
    lift: kind === 'celebrate' ? Math.max(0, progress) * .04 : 0,
    lean: progress * (quiet ? .012 : relaxed ? .035 : .065),
    gesture: quiet ? undefined : { kind, progress },
  }
}

/** Small hand-held props stay inside each person's depth-sorted draw pass. */
export function drawActivityGesture(context: CanvasRenderingContext2D, gesture: ActivityGesture, unit: number) {
  const { kind, progress } = gesture
  context.save()
  context.translate(.07 * unit, -.32 * unit)
  context.lineCap = 'round'
  context.lineJoin = 'round'
  context.lineWidth = Math.max(1, unit * .045)
  context.strokeStyle = '#4c3324'
  if (kind === 'chop' || kind === 'mine' || kind === 'build' || kind === 'farm') {
    context.rotate(-.65 + progress * .65)
    context.beginPath(); context.moveTo(0, 0); context.lineTo(0, -.34 * unit); context.stroke()
    context.fillStyle = '#b3aa9b'
    const width = (kind === 'mine' || kind === 'farm' ? .25 : .16) * unit
    context.fillRect(-width / 2, -.38 * unit, width, .08 * unit)
    context.strokeRect(-width / 2, -.38 * unit, width, .08 * unit)
  } else if (kind === 'experiment' || kind === 'eat') {
    if (kind === 'eat') context.translate(0, -(progress + 1) * .1 * unit)
    context.fillStyle = '#bf8b50'
    context.beginPath(); context.ellipse(0, 0, unit * .17, unit * .09, 0, 0, Math.PI); context.fill(); context.stroke()
    context.fillStyle = kind === 'eat' ? '#d59a4c' : '#8e877c'
    context.beginPath(); context.ellipse(0, 0, unit * .14, unit * .045, 0, 0, Math.PI * 2); context.fill()
  }
  context.restore()
}
