import type { Citizen, Structure } from './api'
import { PersonReference, StructureReference } from './RecordReferences'
import { citizenObservationLabel } from './newcomers'

const format = (value: number) => new Intl.NumberFormat('en-US').format(value)

function BuildingFacts({ structure }: { structure: Structure }) {
  return <dl className="structure-details">
    <div><dt>Started minute</dt><dd>{format(structure.startedMinute)}</dd></div>
    <div><dt>Completed minute</dt><dd>{structure.completedMinute === null ? 'Not completed' : format(structure.completedMinute)}</dd></div>
    <div><dt>Wood</dt><dd>{format(structure.deliveredWood)} / {format(structure.requiredWood)}</dd></div>
    <div><dt>Stone</dt><dd>{format(structure.deliveredStone)} / {format(structure.requiredStone)}</dd></div>
    <div><dt>Work</dt><dd>{format(structure.completedWork)} / {format(structure.requiredWork)}</dd></div>
    <div><dt>Condition</dt><dd>{format(structure.condition / 100)}%</dd></div>
    {structure.capacity !== null && <div><dt>Shelter capacity</dt><dd>{format(structure.capacity)}</dd></div>}
    {structure.storageBonus !== null && <div><dt>Storage bonus</dt><dd>{format(structure.storageBonus)}</dd></div>}
    {structure.constructionMultiplierBasisPoints !== null && <div><dt>Construction multiplier</dt><dd>{format(structure.constructionMultiplierBasisPoints / 10000)}×</dd></div>}
    {structure.cropStage && <div><dt>Crop stage</dt><dd>{structure.cropStage}</dd></div>}
  </dl>
}

function Occupants({ structure, citizens, onPerson }: { structure: Structure; citizens: Citizen[]; onPerson: (id: string) => void }) {
  return <section className="building-occupants" aria-label={`Current occupants of ${structure.type} #${structure.structureId}`}>
    <h4>Current occupants · {format(structure.currentOccupantIds.length)}</h4>
    {structure.currentOccupantIds.length === 0 ? <p>No current occupants recorded.</p> : <ul className="building-people">
      {structure.currentOccupantIds.map(id => {
        const person = citizens.find(value => value.citizenId === id)
        return <li key={id}><PersonReference id={id} citizens={citizens} onPerson={onPerson} />{person && <small>{citizenObservationLabel(person)}</small>}</li>
      })}
    </ul>}
  </section>
}

type BuildingProps = { citizens: Citizen[]; onPerson: (id: string) => void }

export function BuildingLedger({ structures, activeProject, citizens, error, onPerson, onStructure }: BuildingProps & { structures: Structure[] | null; activeProject: Structure | null; error: string | null; onStructure: (id: string) => void }) {
  return <section className="structures" aria-labelledby="structures-heading">
    <div className="section-heading"><span className="section-kicker">Building ledger</span><h2 id="structures-heading">Shelter, stores, and workshop</h2></div>
    {structures === null && !error && <p role="status">Loading structures…</p>}
    {activeProject && <article className="active-project">
      <div><span className="section-kicker">Active construction</span><h3>{activeProject.type} at ({activeProject.location.x}, {activeProject.location.y})</h3><button type="button" className="record-link" onClick={() => onStructure(activeProject.structureId)}>View active building</button></div>
      <div className="project-progress"><span>Materials: wood {activeProject.deliveredWood}/{activeProject.requiredWood} · stone {activeProject.deliveredStone}/{activeProject.requiredStone}</span><progress value={activeProject.completedWork} max={activeProject.requiredWork} aria-label={`${activeProject.type} work progress`} /><span>{format(activeProject.completedWork)} / {format(activeProject.requiredWork)} work</span></div>
    </article>}
    {structures?.length === 0 && <p>No structures have been raised.</p>}
    {structures && <div className="structure-grid">{structures.map(structure => <article className={`structure-card ${structure.status === 'Complete' ? 'is-complete' : ''}`} key={structure.structureId}>
      <div className="structure-card-heading"><h3><StructureReference id={structure.structureId} structures={structures} onStructure={onStructure} /></h3><span>{structure.status === 'Complete' ? 'Complete' : 'Building'}</span></div>
      <p>At ({structure.location.x}, {structure.location.y})</p>
      <BuildingFacts structure={structure} />
      <Occupants structure={structure} citizens={citizens} onPerson={onPerson} />
    </article>)}</div>}
  </section>
}

export function BuildingDetails({ structure, citizens, onPerson, onHistory }: BuildingProps & { structure: Structure; onHistory: (id: string) => void }) {
  return <section className="building-record" aria-labelledby="building-heading">
    <div className="section-heading"><span className="section-kicker">Building record</span><h2 id="building-heading" tabIndex={-1}>{structure.type} #{structure.structureId}</h2></div>
    <article className={`structure-card ${structure.status === 'Complete' ? 'is-complete' : ''}`}>
      <p>{structure.status === 'Complete' ? 'Complete' : 'Under construction'} · At ({structure.location.x}, {structure.location.y})</p>
      <BuildingFacts structure={structure} />
      <Occupants structure={structure} citizens={citizens} onPerson={onPerson} />
      <section className="building-contributions" aria-label="Recorded construction contributions"><h4>Construction contributors</h4>
        {structure.contributions.length === 0 ? <p>No construction contributions recorded.</p> : <ul className="building-people">
          {structure.contributions.map(value => <li key={value.citizenId}><PersonReference id={value.citizenId} citizens={citizens} onPerson={onPerson} /><span>Work {format(value.constructionWork)} · wood {format(value.woodDelivered)} · stone {format(value.stoneDelivered)}</span></li>)}
        </ul>}
      </section>
      <button type="button" className="observer-button" onClick={() => onHistory(structure.structureId)}>View building history</button>
    </article>
  </section>
}
