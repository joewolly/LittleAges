import type { Map } from '../api'
import type { LivingWorld } from '../living'
import { isM12OwnedGood, isUnifiedLivingRules } from '../living'
import { worldToScene } from './visuals'

/** Geometry depicts canonical state; no visual animation advances gameplay. */
export function LivingScene({ map, world }: { map: Map; world: LivingWorld | null }) {
  if (!world) return null
  const stores = worldToScene(map, map.startingSite.x, map.startingSite.y)
  const unified = isUnifiedLivingRules(world.rulesVersion)
  return <group>
    {world.stock.filter(s => s.quantity > 0 && (!unified || !isM12OwnedGood(s.good))).map((s, index) => <group key={`supply-${s.good}`} position={[stores.x + (index % 3 - 1) * .28, stores.y, stores.z + Math.floor(index / 3) * .28]}>
      <mesh position={[0, Math.min(.65, .12 + s.quantity / 300), 0]} castShadow><boxGeometry args={[.2, Math.min(1.3, .24 + s.quantity / 150), .2]} /><meshStandardMaterial color={s.good === 'Grain' || s.good === 'PreservedFood' ? '#c9ab65' : s.good === 'Fuel' ? '#735847' : '#b4bcaa'} /></mesh>
    </group>)}
    {world.weather === 'Rain' && Array.from({ length: 24 }, (_, i) => <mesh key={`rain-${i}`} position={[stores.x + (i % 6 - 2.5) * 2.1, stores.y + 1.4 + i % 3, stores.z + (Math.floor(i / 6) - 1.5) * 2.4]} rotation={[0, 0, -.2]}><boxGeometry args={[.025, .5, .025]} /><meshBasicMaterial color="#a9c1d0" transparent opacity={.55} /></mesh>)}
    {world.fields.map(field => { const p = worldToScene(map, field.location.x, field.location.y); const height = 0.08 + field.growth / 50000; return <group key={`field-${field.id}`} position={[p.x, p.y + 0.04, p.z]}>
      <mesh receiveShadow rotation={[-Math.PI / 2, 0, 0]}><planeGeometry args={[0.92, 0.92]} /><meshStandardMaterial color={field.moisture < 1500 ? '#9b8059' : '#664b31'} /></mesh>
      {[0, 1, 2, 3].map(row => <mesh key={row} position={[-0.3 + row * 0.2, height / 2, 0]} castShadow><boxGeometry args={[0.08, height, 0.78]} /><meshStandardMaterial color={field.yieldRemaining > 0 ? '#dcba54' : field.condition < 3000 ? '#9c8750' : '#688749'} /></mesh>)}</group> })}
    {world.facilities.map(facility => { const p = worldToScene(map, facility.location.x, facility.location.y); return <group key={`facility-${facility.id}`} position={[p.x, p.y, p.z]}>
      <mesh position={[0, 0.24, 0]} castShadow><boxGeometry args={[0.8, 0.48, 0.7]} /><meshStandardMaterial color={facility.kind === 'Hearth' ? '#8c6755' : facility.kind === 'Loom' ? '#a87d50' : '#dfd4ad'} /></mesh>
      <mesh position={[0, 0.59, 0]} rotation={[0, Math.PI / 4, 0]} castShadow><coneGeometry args={[0.65, 0.35, 4]} /><meshStandardMaterial color={facility.kind === 'CareHouse' ? '#778e78' : '#b27547'} /></mesh>
      {facility.kind === 'Hearth' && <mesh position={[0.2, 0.75, 0]}><cylinderGeometry args={[0.08, 0.1, 0.4, 6]} /><meshStandardMaterial color="#64564c" /></mesh>}</group> })}
    {world.animals.map(animal => { const p = worldToScene(map, animal.location.x, animal.location.y); return <group key={`animal-${animal.id}`} position={[p.x, p.y, p.z]}>
      <mesh position={[0, 0.2, 0]} castShadow><boxGeometry args={[0.38, 0.22, 0.18]} /><meshStandardMaterial color={animal.predator ? '#626364' : '#987350'} /></mesh>
      <mesh position={[0.2, 0.3, 0]} castShadow><boxGeometry args={[0.14, 0.18, 0.13]} /><meshStandardMaterial color={animal.predator ? '#626364' : '#a9855e'} /></mesh>
      {[-0.13, 0.13].map(x => <mesh key={x} position={[x, 0.07, 0]}><boxGeometry args={[0.06, 0.14, 0.17]} /><meshStandardMaterial color="#665443" /></mesh>)}</group> })}
    {world.orders.filter(o => o.citizenId !== null).slice(0, 64).map(order => { const p = worldToScene(map, order.location.x, order.location.y); return <mesh key={`work-${order.id}`} position={[p.x, p.y + 0.06, p.z]} rotation={[-Math.PI / 2, 0, 0]}><ringGeometry args={[0.42, 0.46, 20]} /><meshBasicMaterial color="#f0d38c" transparent opacity={0.65} /></mesh> })}
  </group>
}
