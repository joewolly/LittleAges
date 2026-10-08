import type { RulesUpgradeStatus } from './api'

export function RulesUpgradeNotice({ upgrade }: { upgrade?: RulesUpgradeStatus }) {
  if (!upgrade || (upgrade.state !== 'Failed' && upgrade.state !== 'Unsupported')) return null
  return <div className="notice rules-upgrade-notice" role={upgrade.state === 'Failed' ? 'alert' : 'status'}>
    <strong>{upgrade.state === 'Failed' ? 'World feature update deferred' : 'World feature update unavailable'}</strong>
    <p>{upgrade.message}</p>
  </div>
}
