import type { Map, Structure } from '../api'
import { worldToScene } from './visuals'

export function FarmModel({ map, structure }: { map: Map; structure: Structure }) {
  const point = worldToScene(map, structure.location.x, structure.location.y, .04)
  const stage = structure.status === 'Complete' ? structure.cropStage : 'Fallow'
  const growing = stage === 'Planted' || stage === 'Growing' || stage === 'Harvest'
  const height = stage === 'Planted' ? .12 : stage === 'Growing' ? .35 : .55
  const color = stage === 'Harvest' ? '#edc454' : '#7cb344'
  return <group position={[point.x, point.y, point.z]}>
    <mesh receiveShadow><boxGeometry args={[1.8, .12, 1.8]} /><meshStandardMaterial color={stage === 'Dormant' ? '#a18a60' : '#735032'} /></mesh>
    {[-.64, -.32, 0, .32, .64].map(x => <group key={x} position={[x, .09, 0]}>
      <mesh receiveShadow><boxGeometry args={[.14, .07, 1.5]} /><meshStandardMaterial color="#493823" /></mesh>
      {growing && [-.6, -.3, 0, .3, .6].map(z => <mesh key={z} position={[0, height / 2, z]} castShadow>
        <boxGeometry args={[.13, height, .15]} /><meshStandardMaterial color={color} roughness={1} />
      </mesh>)}
    </group>)}
    {[-.9, .9].flatMap(x => [-.9, .9].map(z => <mesh key={`${x}:${z}`} position={[x, .24, z]} castShadow><boxGeometry args={[.1, .55, .1]} /><meshStandardMaterial color="#ba9053" /></mesh>))}
  </group>
}

export function GranaryModel({ map, structure }: { map: Map; structure: Structure }) {
  const point = worldToScene(map, structure.location.x, structure.location.y, .05)
  const built = structure.status === 'Complete'
  return <group position={[point.x, point.y, point.z]} scale={.9}>
    <mesh position={[0, .2, 0]} receiveShadow castShadow><boxGeometry args={[1.65, .4, 1.5]} /><meshStandardMaterial color="#8a7453" /></mesh>
    <mesh position={[0, .88, 0]} castShadow><boxGeometry args={[1.5, 1.1, 1.3]} /><meshStandardMaterial color={built ? '#d4a05b' : '#8d734e'} /></mesh>
    {built && <mesh position={[0, 1.7, 0]} rotation={[0, Math.PI / 4, 0]} castShadow><coneGeometry args={[1.4, .9, 4]} /><meshStandardMaterial color="#b57032" /></mesh>}
    <mesh position={[0, .74, .66]}><boxGeometry args={[.52, .8, .06]} /><meshStandardMaterial color="#5a3d2b" /></mesh>
    {[-.52, .52].map(x => <mesh key={x} position={[x, .58, .86]} castShadow><cylinderGeometry args={[.17, .21, .5, 8]} /><meshStandardMaterial color="#e2c58c" /></mesh>)}
  </group>
}

export function MarketplaceModel({ map, structure }: { map: Map; structure: Structure }) {
  const point = worldToScene(map, structure.location.x, structure.location.y, .04)
  return <group position={[point.x, point.y, point.z]}>
    <mesh receiveShadow><boxGeometry args={[1.9, .12, 1.9]} /><meshStandardMaterial color="#ba9d72" /></mesh>
    {[-.75, .75].flatMap(x => [-.65, .65].map(z => <mesh key={`${x}:${z}`} position={[x, .65, z]} castShadow><boxGeometry args={[.12, 1.3, .12]} /><meshStandardMaterial color="#785638" /></mesh>))}
    <mesh position={[0, .5, .4]} castShadow><boxGeometry args={[1.7, .15, .65]} /><meshStandardMaterial color="#bc894a" /></mesh>
    {structure.status === 'Complete' && [-.6, -.2, .2, .6].map((x, i) => <mesh key={x} position={[x, 1.38, 0]} rotation={[-.08, 0, 0]} castShadow><boxGeometry args={[.4, .12, 1.65]} /><meshStandardMaterial color={i % 2 ? '#f0d59a' : '#b85569'} /></mesh>)}
    {[-.55, 0, .55].map((x, i) => <mesh key={x} position={[x, .7, .4]} castShadow><boxGeometry args={[.38, .25, .38]} /><meshStandardMaterial color={['#cba64a', '#845333', '#92918a'][i]} /></mesh>)}
  </group>
}
