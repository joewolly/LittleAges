import { festivalStatus, type LivingWorld } from './living'

export function FestivalRecords({ world, onSelectCitizen, name }: { world: LivingWorld; onSelectCitizen: (id: string) => void; name: (id: string) => string }) {
  if (!world.capabilities.includes('festivals')) return null
  return <section className="festival-records" aria-label="Harvest festivals"><h3>Harvest festivals</h3>
    <p>A shared afternoon after the harvest. In lean years, neighbors gather without spending feast food.</p>
    <div className="festival-list">{world.festivals?.map(festival => { const guests = festival.attendance.filter(a => a.benefitsGranted); return <article key={festival.settlementId}>
      <span className="section-kicker">Settlement {festival.settlementId}</span><h4>{festivalStatus(festival)}</h4>
      <p>Year {festival.year} · month 9, day {festival.settlementId === '1' ? 1 : 8} · noon–18:00</p>
      {festival.started && <p>{guests.length} neighbors joined · {festival.consumedFood} food shared{festival.mode === 'Gathering' ? ' · a modest gathering' : ''}</p>}
      {guests.length > 0 && <div className="festival-guests">{guests.map(attendee => <button className="observer-button" type="button" key={attendee.citizenId} onClick={() => onSelectCitizen(attendee.citizenId)}>{name(attendee.citizenId)}</button>)}</div>}
    </article> })}</div>
  </section>
}
