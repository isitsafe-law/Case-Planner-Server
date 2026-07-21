// Linear escalation sequence for "who currently has the file" (CurrentHolder), matching the case
// editor's dropdown order in App.tsx. `Other` is a deliberate non-sequential catch-all - it doesn't
// fit "before/after" semantics, so it's rendered as a separate badge rather than a fifth step.
export const HOLDER_STEPS = ['Legal Assistant', 'Attorney', 'Deputy Chief Counsel', 'Chief Counsel'] as const
export type HolderStep = (typeof HOLDER_STEPS)[number]
export const OTHER_HOLDER = 'Other'

export type HolderStepState = 'completed' | 'current' | 'upcoming'

// Index of `holder` within the linear sequence, or -1 for anything off the line (Other, or an
// unset/unrecognized value). Pulled out for direct unit testing.
export function holderStepIndex(holder: string | null | undefined): number {
  if (!holder) return -1
  return HOLDER_STEPS.indexOf(holder as HolderStep)
}

export function holderStepState(stepIndex: number, currentIndex: number): HolderStepState {
  if (currentIndex < 0) return 'upcoming'
  if (stepIndex < currentIndex) return 'completed'
  if (stepIndex === currentIndex) return 'current'
  return 'upcoming'
}

export function HolderPipelineStepper({
  currentHolder,
  onSelect,
}: {
  currentHolder: string | null | undefined
  onSelect: (holder: string) => void
}) {
  const currentIndex = holderStepIndex(currentHolder)
  const isOtherActive = currentIndex === -1 && !!currentHolder

  return (
    <div className="holder-stepper" role="group" aria-label="Pipeline holder">
      <ol className="holder-stepper-steps">
        {HOLDER_STEPS.map((step, index) => {
          const state = holderStepState(index, currentIndex)
          return (
            <li key={step} className="holder-step-item">
              {index > 0 && (
                <span
                  className={`holder-step-connector${index <= currentIndex ? ' holder-step-connector-filled' : ''}`}
                  aria-hidden="true"
                />
              )}
              <button
                type="button"
                className={`holder-step holder-step-${state}`}
                aria-current={state === 'current' ? 'step' : undefined}
                onClick={() => onSelect(step)}
              >
                {step}
              </button>
            </li>
          )
        })}
      </ol>
      <button
        type="button"
        className={`holder-step holder-step-other ${isOtherActive ? 'holder-step-current' : 'holder-step-upcoming'}`}
        aria-current={isOtherActive ? 'step' : undefined}
        onClick={() => onSelect(OTHER_HOLDER)}
      >
        {OTHER_HOLDER}
      </button>
    </div>
  )
}
