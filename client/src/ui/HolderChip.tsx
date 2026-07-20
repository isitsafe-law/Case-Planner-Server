import { StatusChip } from './StatusChip'

export function HolderChip({ holder }: { holder: string }) {
  return <StatusChip tone="neutral">Holder: {holder}</StatusChip>
}
