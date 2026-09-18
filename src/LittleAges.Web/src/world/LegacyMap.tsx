import { useEffect, useRef } from 'react'
import type { Citizen, Map, Structure } from '../api'

const terrainColors: Record<number, string> = { 1: '#89b8c5', 2: '#d3c78e', 3: '#76966a', 4: '#968873', 5: '#4f785b' }
const structureColors: Record<Structure['type'], string> = { Shelter: '#c76848', Stockpile: '#805c3d', Workshop: '#75569a' }

export function LegacyMap({ map, structures, citizens }: { map: Map; structures: Structure[]; citizens: Citizen[] }) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  useEffect(() => {
    const canvas = canvasRef.current
    const context = canvas?.getContext('2d')
    if (!canvas || !context) return
    const padding = 20
    const scale = Math.max(2, Math.min(24, Math.floor(Math.min((canvas.width - padding * 2) / map.width, (canvas.height - padding * 2) / map.height))))
    const drawWidth = map.width * scale
    const drawHeight = map.height * scale
    const offsetX = Math.floor((canvas.width - drawWidth) / 2)
    const offsetY = Math.floor((canvas.height - drawHeight) / 2)
    context.fillStyle = '#f7f0e5'
    context.fillRect(0, 0, canvas.width, canvas.height)
    for (let y = 0; y < map.height; y += 1) for (let x = 0; x < map.width; x += 1) {
      context.fillStyle = terrainColors[map.terrain[y * map.width + x]]
      context.fillRect(offsetX + x * scale, offsetY + y * scale, scale, scale)
    }
    context.strokeStyle = '#f7f0e5'
    context.lineWidth = Math.max(1, scale / 12)
    context.strokeRect(offsetX + map.startingSite.x * scale, offsetY + map.startingSite.y * scale, scale, scale)
    for (const structure of structures) {
      const x = offsetX + structure.location.x * scale
      const y = offsetY + structure.location.y * scale
      context.fillStyle = structureColors[structure.type]
      context.globalAlpha = structure.status === 'Complete' ? 1 : 0.52
      context.fillRect(x + scale * 0.18, y + scale * 0.18, scale * 0.64, scale * 0.64)
      context.globalAlpha = 1
    }
    context.fillStyle = '#302a24'
    for (const citizen of citizens.filter(entry => entry.isAlive)) context.fillRect(offsetX + citizen.location.x * scale + scale * 0.42, offsetY + citizen.location.y * scale + scale * 0.42, Math.max(2, scale * 0.18), Math.max(2, scale * 0.18))
  }, [citizens, map, structures])

  return <div className="world-fallback" role="img" aria-label={`Settlement map, ${map.width} by ${map.height} tiles. Colored squares mark structures and dark points mark living citizens.`}><canvas ref={canvasRef} width="960" height="600" /></div>
}
