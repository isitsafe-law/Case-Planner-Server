export type TypeChipKind = 'deadline' | 'task' | 'event' | 'service' | 'discovery'

const typeChipLabels: Record<TypeChipKind, string> = {
  deadline: 'Deadline',
  task: 'Task',
  event: 'Event',
  service: 'Service',
  discovery: 'Discovery',
}

export function TypeChip({ kind }: { kind: TypeChipKind }) {
  return <span className={`ui-typechip ui-typechip-${kind}`}>{typeChipLabels[kind]}</span>
}
