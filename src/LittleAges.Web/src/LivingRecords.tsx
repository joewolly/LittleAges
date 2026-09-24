import type { Citizen } from './api'
import { isM12OwnedGood, isUnifiedLivingRules, livingLabel, livingWorkStage, type LivingWorld } from './living'

const percent = (value: number) => `${Math.round(value / 100)}%`
const techniques = ['', 'Cultivation', 'Preservation', 'Toolmaking', 'Textiles', 'Care']
const weatherNames = ['', 'Fair', 'Rain', 'Drought', 'Cold spell']

export function LivingCitizenRecord({ world, citizenId, citizens = [] }: { world: LivingWorld | null; citizenId: string; citizens?: Citizen[] }) {
  const person = world?.people.find(p => p.citizenId === citizenId)
  if (!world || !person) return null
  const order = world.orders.find(o => o.citizenId === citizenId)
  const name = (id: string) => citizens.find(c => c.citizenId === id)?.name ?? `Citizen ${id}`
  return <section className="living-person" aria-label="Personal life"><h4>Personal life</h4><dl className="citizen-details">
    <div><dt>Long-term goal</dt><dd>{livingLabel(person.goal)}</dd></div><div><dt>Mood</dt><dd>{percent(person.mood)}</dd></div><div><dt>Stress</dt><dd>{percent(person.stress)}</dd></div>
    <div><dt>Injury</dt><dd>{percent(person.injury)}</dd></div><div><dt>Illness</dt><dd>{percent(person.illness)}</dd></div><div><dt>Tool condition</dt><dd>{percent(person.toolCondition)}</dd></div><div><dt>Clothing condition</dt><dd>{percent(person.clothingCondition)}</dd></div>
  </dl><p><strong>Current work:</strong> {order ? `${livingLabel(order.kind)} · ${livingWorkStage(order)} · ${Math.round(order.workDone / order.requiredWork * 100)}%` : 'Meeting personal needs or choosing work'}</p>
    <p><strong>Knowledge:</strong> {person.knowledge.map(livingLabel).join(', ') || 'Learning through practice'}</p>
    <h4>Recent experiences</h4>{person.experiences.length ? <ul>{person.experiences.slice(-6).reverse().map((e, i) => <li key={`${e.minute}-${i}`}>{livingLabel(e.kind)}{e.otherCitizenId ? ` · ${name(e.otherCitizenId)}` : ''} · day {Math.floor(e.minute / 1440) + 1}</li>)}</ul> : <p>No significant recent experiences.</p>}
  </section>
}

export function LivingRecords({ world, citizens, foodStored, onSelectCitizen }: { world: LivingWorld | null; citizens: Citizen[]; foodStored?: number; onSelectCitizen: (id: string) => void }) {
  if (!world) return <p>This world uses its original simulation rules. Living settlement systems are available in new v0.2 worlds.</p>
  const unified = isUnifiedLivingRules(world.rulesVersion)
  const name = (id: string | null) => citizens.find(c => c.citizenId === id)?.name ?? 'Unassigned'
  return <section className="living-records" aria-labelledby="living-heading"><div className="section-heading"><span className="section-kicker">Living settlement</span><h2 id="living-heading">A world at work</h2><p>{world.age} life · {livingLabel(world.weather)} · {world.temperature}°C · rain {world.rainfall}%</p></div>
    <dl className="citizen-details"><div><dt>Completed work</dt><dd>{world.completedOrders.toLocaleString()}</dd></div>{!unified && <div><dt>Harvested grain</dt><dd>{world.foodHarvested.toLocaleString()}</dd></div>}<div><dt>Prepared food</dt><dd>{world.foodPrepared.toLocaleString()}</dd></div><div><dt>Care given</dt><dd>{world.careGiven.toLocaleString()}</dd></div></dl>
    <h3>Shared supplies</h3><div className="living-supplies">{!unified && foodStored !== undefined && <span>Ready food <strong>{foodStored.toLocaleString()}</strong></span>}{world.stock.filter(s => s.good !== 'Meal' && (!unified || !isM12OwnedGood(s.good))).map(s => <span key={s.good}>{livingLabel(s.good)} <strong>{s.quantity.toLocaleString()}</strong></span>)}</div>
    <p>Seasonal preparation targets 1,000 preserved food units per person for winter and a difficult spring.</p>
    <h3>Work and cooperation</h3>{world.orders.length === 0 && <p>No work is waiting.</p>}<div className="living-work-list">{world.orders.map(order => <article key={order.id}><div><strong>{livingLabel(order.kind)}{order.technique ? ` · ${livingLabel(order.technique)}` : ''}</strong><span>{Math.round(order.workDone / order.requiredWork * 100)}%</span></div><progress value={order.workDone} max={order.requiredWork} aria-label={`${livingLabel(order.kind)} progress`} />
      {order.citizenId ? <button className="observer-button" type="button" onClick={() => onSelectCitizen(order.citizenId!)}>{name(order.citizenId)} · {livingWorkStage(order)}</button> : <p>{order.blockedReason || 'Waiting for a worker'}</p>}
      {order.subjectId && ['Care', 'Teach', 'RepairRelationship'].includes(order.kind) && <p>For {name(order.subjectId)}</p>}
      <small>{order.ingredients.length ? `Inputs: ${order.ingredients.map(i => `${i.quantity} ${livingLabel(i.resource)}`).join(', ')}` : 'Requires time and skill'} · ({order.location.x}, {order.location.y})</small></article>)}</div>
    <h3>{unified ? 'Facilities and wildlife' : 'Fields and wildlife'}</h3><p>{!unified && `${world.fields.length} fields · `}{world.facilities.length} production facilities · {world.animals.length} wild animals</p>{!unified && world.fields.map(field => <p key={field.id}>Field {field.id}: growth {percent(field.growth)}, moisture {percent(field.moisture)}, condition {percent(field.condition)}{field.yieldRemaining > 0 ? ` · ${field.yieldRemaining} grain ready` : ''}</p>)}
    <h3>Knowledge in the community</h3><ul>{[...new Set(world.people.filter(p => !p.deathObserved).flatMap(p => p.knowledge))].map(k => <li key={k}>{livingLabel(k)} · {world.people.filter(p => !p.deathObserved && p.knowledge.includes(k)).length} living practitioners</li>)}</ul>
    <h3>Recent consequences</h3><p>Latest {world.facts.length} of {world.totalFacts} factual events.</p><ol className="living-facts">{world.facts.map(f => <li key={f.id}><strong>{livingLabel(f.kind)}</strong>{f.kind.startsWith('Technique') ? `: ${techniques[f.value] ?? ''}` : f.kind === 'WeatherChanged' ? `: ${weatherNames[f.value] ?? ''}` : ''} · day {Math.floor(f.minute / 1440) + 1}{f.citizenId ? ` · ${name(f.citizenId)}` : ''}{f.kind === 'TechniqueTaught' && f.relatedId ? ` · taught by ${name(f.relatedId)}` : ''}</li>)}</ol>
  </section>
}
