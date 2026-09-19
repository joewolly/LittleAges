import { useEffect, useState } from 'react'
import type { Citizen, Map, Structure } from '../api'
import { WorldViewport } from './WorldViewport'
import './artSlice.css'

// Isolated presentation fixture. This module is imported only by the Vite DEV
// entrypoint and never connects to, writes to, or advances a saved world.
const map: Map = { width: 40, height: 40, terrain: [], elevation: [], resources: [], startingSite: { x: 20, y: 20 } }
for (let y = 0; y < map.height; y++) for (let x = 0; x < map.width; x++) {
  const bank = 9 + Math.sin(y * 0.19) * 2
  map.terrain.push(x < bank ? 1 : x < bank + 1.6 ? 2 : 3)
  map.elevation.push(x < bank ? 180 : 300 + Math.round(Math.max(0, x - 25) * 12))
}
const nodes: Array<[number, number, 'Wood' | 'Food' | 'Stone']> = [
  [13, 15, 'Wood'], [15, 12, 'Wood'], [19, 12, 'Wood'], [24, 13, 'Wood'], [28, 16, 'Wood'], [29, 20, 'Wood'],
  [30, 24, 'Wood'], [15, 27, 'Wood'], [19, 29, 'Wood'], [26, 28, 'Wood'], [12, 22, 'Wood'],
  [14, 18, 'Food'], [13, 19, 'Food'], [14, 24, 'Food'], [27, 23, 'Stone'], [28, 24, 'Stone'], [25, 15, 'Stone'],
]
map.resources = nodes.map(([x, y, resourceType], i) => ({ resourceNodeId: String(i + 1), resourceType, location: { x, y }, maximumQuantity: 100, regenerationPotential: 1 }))
const structures: Structure[] = (['Shelter', 'Workshop', 'Stockpile', 'Shelter'] as const).map((type, i) => ({
  structureId: String(101 + i), type, status: i === 3 ? 'UnderConstruction' : 'Complete', location: [{ x: 18, y: 18 }, { x: 23, y: 18 }, { x: 23, y: 23 }, { x: 18, y: 24 }][i],
  startedMinute: 0, completedMinute: i === 3 ? null : 10, requiredWood: 10, deliveredWood: 10, requiredStone: 4, deliveredStone: 4,
  requiredWork: 20, completedWork: i === 3 ? 10 : 20, condition: 10000, capacity: 4, storageBonus: null, constructionMultiplierBasisPoints: null, currentOccupantIds: [], contributions: [],
}))
const activities: Citizen['currentAction'][] = ['GatherFood', 'GatherWood', 'GatherStone', 'Build', 'Socialize', 'Socialize', 'Rest', 'HaulConstruction', 'None']
const positions = [[14, 19], [13, 16], [27, 24], [18, 25], [20, 21], [21, 21], [17, 19], [23, 22], [20, 24]]
const baseCitizens: Citizen[] = activities.map((currentAction, i) => ({
  citizenId: String(i + 1), name: ['Mira', 'Rowan', 'Bram', 'Ada', 'Finn', 'Nell', 'Hugo', 'Iris', 'Leo'][i], age: 26, lifeStage: 'Adult', location: { x: positions[i][0], y: positions[i][1] },
  health: 10000, currentAction, actionSequence: 1, actionStartedMinute: 0, actionCompletesMinute: 1000, target: null, isAlive: true, deathMinute: null, deathCause: null,
  hunger: 0, rest: 0, shelter: 0, social: 0, actionPhase: 'Perform', carriedResource: i === 7 ? 'Wood' : null, carriedQuantity: i === 7 ? 4 : 0, targetResourceNodeId: null,
  homeStructureId: '101', targetStructureId: null, occupation: 'Generalist', lifetimeWorkActivity: { foragingMinutes: 0, woodcuttingMinutes: 0, stoneworkingMinutes: 0, constructionMinutes: 0, haulingMinutes: 0 },
  founderOrdinal: i, parentAId: null, parentBId: null, partnerId: null, householdId: null, childrenIds: [], targetCitizenId: null, movementPlan: null,
}))

export function ArtSlice() {
  const [paused, setPaused] = useState(false)
  const [minute, setMinute] = useState(0)
  const [selected, setSelected] = useState<string | null>('4')
  const [detail, setDetail] = useState<'full' | 'reduced' | 'auto'>('full')
  useEffect(() => {
    if (paused) return
    const timer = window.setInterval(() => setMinute(value => value + 0.1), 100)
    return () => window.clearInterval(timer)
  }, [paused])
  const leg = Math.floor(minute / 8)
  const start = leg * 8
  const fromX = leg % 2 === 0 ? 20 : 24
  const toX = leg % 2 === 0 ? 24 : 20
  const citizens = baseCitizens.map(citizen => citizen.citizenId !== '9' ? citizen : { ...citizen, actionPhase: 'TravelToTarget' as const, actionSequence: leg + 1,
    location: { x: fromX, y: 24 }, movementPlan: { actionSequence: leg + 1, observedMinute: minute, waypoints: [{ x: fromX, y: 24, arriveMinute: start }, { x: toX, y: 24, arriveMinute: start + 8 }] } })
  return <main className="art-study">
    <header className="art-study-header"><strong>Little Ages · Art study</strong><span>Isolated presentation scene</span><button onClick={() => setPaused(value => !value)}>{paused ? 'Resume' : 'Pause'}</button>
      <label>Detail <select value={detail} onChange={event => setDetail(event.target.value as typeof detail)}><option value="full">Full lighting</option><option value="reduced">Reduced</option><option value="auto">Automatic</option></select></label>
      <label>Villager <select value={selected ?? ''} onChange={event => setSelected(event.target.value)}>{citizens.map(citizen => <option key={citizen.citizenId} value={citizen.citizenId}>{citizen.name} · {citizen.currentAction}</option>)}</select></label>
    </header>
    <WorldViewport map={map} citizens={citizens} structures={structures} settlement={null} worldSeed="42" operationalSpeed={1} paused={paused} selectedCitizenId={selected} onSelectCitizen={setSelected} previewDetailTier={detail === 'auto' ? undefined : detail} />
  </main>
}
