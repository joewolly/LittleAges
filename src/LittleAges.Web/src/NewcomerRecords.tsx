import type { Citizen } from './api'
import { citizenObservationLabel, isCitizenGuest, isCitizenResident } from './newcomers'

export function CitizenRoster({ citizens, selectedId, onSelect }: { citizens: Citizen[]; selectedId: string | null; onSelect: (id: string) => void }) {
  const groups = [
    { label: 'Residents', people: citizens.filter(c => isCitizenResident(c) && c.isAlive) },
    { label: 'Visitors', people: citizens.filter(isCitizenGuest) },
    { label: 'Archives', people: citizens.filter(c => !c.isAlive || c.newcomer?.phase === 'Departed') },
  ]
  return <div className="citizen-list" role="list" aria-label="Citizens">{groups.filter(group => group.people.length > 0).map(group => <div key={group.label}>
    <h3>{group.label} · {group.people.length}</h3>{group.people.map(citizen => <button type="button" role="listitem" aria-label={`${citizen.name}, ${citizen.lifeStage}, ${citizen.currentAction}`} className={citizen.citizenId === selectedId ? 'is-selected' : ''} key={citizen.citizenId} onClick={() => onSelect(citizen.citizenId)}>
      <strong>{citizen.name}</strong><span>{citizen.lifeStage} · {citizen.newcomer ? citizenObservationLabel(citizen) : citizen.occupation}</span><small>{citizen.currentAction}</small>
    </button>)}</div>)}</div>
}

export function NewcomerRecord({ citizen }: { citizen: Citizen }) {
  const newcomer = citizen.newcomer
  if (!newcomer) return null
  const minute = (value: number | null) => value === null ? 'Not recorded' : `Minute ${value.toLocaleString('en-US')}`
  return <section className="card-section" aria-label="External origin"><h4>External origin</h4><p>{citizenObservationLabel(citizen)}. Birth date and external parents are unknown; age is estimated.</p>
    <dl className="citizen-details"><div><dt>First observed</dt><dd>{minute(newcomer.firstSeenMinute)}</dd></div><div><dt>Arrived at settlement</dt><dd>{minute(newcomer.visitingStartedMinute)}</dd></div><div><dt>Joined as resident</dt><dd>{minute(newcomer.joinedMinute)}</dd></div><div><dt>Departed</dt><dd>{minute(newcomer.departedMinute)}</dd></div><div><dt>Host settlement</dt><dd>{newcomer.hostSettlementId}</dd></div>
      {isCitizenGuest(citizen) && <><div><dt>Visit deadline</dt><dd>{minute(newcomer.stayDeadlineMinute)}</dd></div><div><dt>Own provisions</dt><dd>{newcomer.provisionsRemaining}</dd></div><div><dt>Guest shelter</dt><dd>{newcomer.shelterStructureId}</dd></div></>}
    </dl>{newcomer.phase === 'Departed' && <p>Last observed alive on departure. Current whereabouts and condition are unknown.</p>}
  </section>
}
