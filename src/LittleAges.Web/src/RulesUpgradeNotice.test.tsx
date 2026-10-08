import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { parseStatus, type RulesUpgradeStatus } from './api'
import { RulesUpgradeNotice } from './RulesUpgradeNotice'

afterEach(cleanup)
const upgrade: RulesUpgradeStatus = { state: 'Failed', sourceRules: 'm14-rng1-migration1', targetRules: 'm17-rng1-newcomers1', activationMinute: null,
  message: 'Your world continues safely with its previous rules.' }

describe('world feature update status', () => {
  it('accepts current status while tolerating older servers and malformed metadata', () => {
    expect(parseStatus({ state: 'Running' }).rulesUpgrade).toBeUndefined()
    expect(parseStatus({ simulationRulesVersion: upgrade.sourceRules, rulesUpgrade: upgrade }).rulesUpgrade).toEqual(upgrade)
    expect(parseStatus({ rulesUpgrade: { ...upgrade, activationMinute: -1 } }).rulesUpgrade).toBeUndefined()
    expect(parseStatus({ rulesUpgrade: { ...upgrade, state: 'MadeUp' } }).rulesUpgrade).toBeUndefined()
  })
  it('shows a clear failure notice without diagnostic paths or technical identifiers', () => {
    render(<RulesUpgradeNotice upgrade={upgrade} />)
    expect(screen.getByRole('alert')).toHaveTextContent('World feature update deferred')
    expect(screen.getByRole('alert')).toHaveTextContent(upgrade.message)
    expect(screen.queryByText(upgrade.sourceRules)).not.toBeInTheDocument()
  })
  it('reports unavailable upgrades without blocking observation', () => {
    render(<RulesUpgradeNotice upgrade={{ ...upgrade, state: 'Unsupported' }} />)
    expect(screen.getByRole('status')).toHaveTextContent('World feature update unavailable')
  })
  it('keeps successful and preserved worlds quiet', () => {
    const { rerender, container } = render(<RulesUpgradeNotice />)
    for (const state of ['Current', 'Preserved', 'Upgraded'] as const) {
      rerender(<RulesUpgradeNotice upgrade={{ ...upgrade, state }} />)
      expect(container).toBeEmptyDOMElement()
    }
  })
})
