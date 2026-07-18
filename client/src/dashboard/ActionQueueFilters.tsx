import type { AttorneyDashboardFilters } from './types'

export function ActionQueueFilters({
  filters,
  counties,
  projects,
  onChange,
}: {
  filters: AttorneyDashboardFilters
  counties: string[]
  projects: string[]
  onChange: (next: AttorneyDashboardFilters) => void
}) {
  function set<K extends keyof AttorneyDashboardFilters>(key: K, value: AttorneyDashboardFilters[K]) {
    onChange({ ...filters, [key]: value || undefined })
  }

  return (
    <div className="action-queue-filters" role="search">
      <label>
        Search
        <input
          type="text"
          value={filters.search ?? ''}
          onChange={(e) => set('search', e.currentTarget.value)}
          placeholder="Case, tract, job number..."
          aria-label="Search matters"
        />
      </label>
      <label>
        Matter type
        <select value={filters.matterType ?? ''} onChange={(e) => set('matterType', e.currentTarget.value)} aria-label="Filter by matter type">
          <option value="">All</option>
          <option value="FiledCase">Filed cases</option>
          <option value="PreFilingTract">Pre-filing tracts</option>
        </select>
      </label>
      <label>
        County
        <select value={filters.county ?? ''} onChange={(e) => set('county', e.currentTarget.value)} aria-label="Filter by county">
          <option value="">All</option>
          {counties.map((c) => <option key={c} value={c}>{c}</option>)}
        </select>
      </label>
      <label>
        Project
        <select value={filters.project ?? ''} onChange={(e) => set('project', e.currentTarget.value)} aria-label="Filter by project">
          <option value="">All</option>
          {projects.map((p) => <option key={p} value={p}>{p}</option>)}
        </select>
      </label>
      <label>
        Current holder
        <select value={filters.currentHolder ?? ''} onChange={(e) => set('currentHolder', e.currentTarget.value)} aria-label="Filter by current holder">
          <option value="">All</option>
          <option value="Legal Assistant">Legal Assistant</option>
          <option value="Attorney">Attorney</option>
          <option value="Deputy Chief Counsel">Deputy Chief Counsel</option>
          <option value="Chief Counsel">Chief Counsel</option>
          <option value="Filing Staff">Filing Staff</option>
          <option value="Other">Other</option>
        </select>
      </label>
      <label>
        Priority
        <select value={filters.priority ?? ''} onChange={(e) => set('priority', e.currentTarget.value)} aria-label="Filter by priority">
          <option value="">All</option>
          <option value="Normal">Normal</option>
          <option value="Priority">Priority</option>
          <option value="Rushed">Rushed</option>
        </select>
      </label>
      {(filters.matterType || filters.county || filters.project || filters.currentHolder || filters.priority || filters.search) && (
        <button type="button" onClick={() => onChange({})}>Clear filters</button>
      )}
    </div>
  )
}
