import type { ButtonHTMLAttributes } from 'react'

export type BtnVariant = 'primary' | 'secondary' | 'ghost' | 'danger'
export type BtnSize = 'md' | 'sm'

export function Btn({
  variant = 'secondary',
  size = 'md',
  className,
  type,
  ...rest
}: {
  variant?: BtnVariant
  size?: BtnSize
} & ButtonHTMLAttributes<HTMLButtonElement>) {
  const classes = ['ui-btn', `ui-btn-${variant}`, size === 'sm' ? 'ui-btn-sm' : '', className]
    .filter(Boolean)
    .join(' ')
  return <button type={type ?? 'button'} className={classes} {...rest} />
}
