import type { Map } from '../api'
import type { LivingWorld } from '../living'
import { worldToScene } from './visuals'

/** Temporary decoration follows observed festival state and never changes terrain. */
export function FestivalScene({ map, world }: { map: Map; world: LivingWorld | null }) {
  return <group>{world?.festivals?.filter(f => f.started && !f.finished).map(festival => {
    const p = worldToScene(map, festival.location.x, festival.location.y)
    return <group key={festival.settlementId} position={[p.x, p.y, p.z]}>
      <mesh position={[0, .4, .65]} castShadow><boxGeometry args={[1.55, .12, .65]} /><meshStandardMaterial color="#936848" /></mesh>
      <mesh position={[0, .47, .65]}><boxGeometry args={[1.5, .025, .63]} /><meshStandardMaterial color="#e9d9b2" /></mesh>
      {[-.58, .58].map(x => <mesh key={x} position={[x, .2, .65]} castShadow><boxGeometry args={[.1, .4, .5]} /><meshStandardMaterial color="#72523a" /></mesh>)}
      {festival.mode === 'Feast' && festival.reservedFood > 0 && [-.45, 0, .45].map(x => <group key={x} position={[x, .52, .65]}><mesh><cylinderGeometry args={[.14, .09, .08, 8]} /><meshStandardMaterial color="#b96d47" /></mesh><mesh position={[0, .05, 0]}><sphereGeometry args={[.1, 6, 4]} /><meshStandardMaterial color="#d3af59" /></mesh></group>)}
      {[-1.05, 1.05].map(x => <mesh key={x} position={[x, 1.4, -.55]} castShadow><cylinderGeometry args={[.045, .045, 2.8, 6]} /><meshStandardMaterial color="#795c40" /></mesh>)}
      <mesh position={[0, 2.7, -.55]}><boxGeometry args={[2.1, .018, .018]} /><meshStandardMaterial color="#765d45" /></mesh>
      {['#b65d48', '#d0a44e', '#65867a', '#b65d48', '#d0a44e', '#65867a', '#b65d48'].map((color, i) => <mesh key={i} position={[-.85 + i * .28, 2.58, -.55]} rotation={[0, 0, Math.PI]}><coneGeometry args={[.11, .22, 3]} /><meshStandardMaterial color={color} /></mesh>)}
    </group>
  })}</group>
}
