import type { Citizen, Structure } from './api'

export function PersonReference({ id, citizens, onPerson }: { id: string; citizens: Citizen[]; onPerson: (id: string) => void }) {
  const person = citizens.find(c => c.citizenId === id)
  return person ? <button type="button" className="record-link" onClick={() => onPerson(id)}>{person.name} · #{id}</button> : <span>Citizen #{id} · record unavailable</span>
}

export function StructureReference({ id, structures, onStructure }: { id: string; structures: Structure[]; onStructure: (id: string) => void }) {
  const structure = structures.find(value => value.structureId === id)
  return structure ? <button type="button" className="record-link" onClick={() => onStructure(id)}>{structure.type} #{id}</button> : <span>Structure #{id} · record unavailable</span>
}
