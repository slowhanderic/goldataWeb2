import { Chip } from '@mui/material'
import { isVoid, statusColor } from '../utils/format'

export default function StatusChip({ value, closedValues }: { value: unknown; closedValues?: string[] }) {
  if (value === null || value === undefined || value === '') return null
  return (
    <Chip
      label={String(value)}
      color={statusColor(value, closedValues)}
      variant={isVoid(value) ? 'outlined' : 'filled'}
      sx={isVoid(value) ? { textDecoration: 'line-through' } : undefined}
    />
  )
}
