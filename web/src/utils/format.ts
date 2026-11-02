import type { ChipProps } from '@mui/material'
import type { FieldKind } from '../api/types'

const money = new Intl.NumberFormat('zh-CN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

export function formatMoney(v: unknown): string {
  if (v === null || v === undefined || v === '') return ''
  const n = Number(v)
  return Number.isFinite(n) ? `¥${money.format(n)}` : String(v)
}

export function formatValue(v: unknown, kind: FieldKind): string {
  if (v === null || v === undefined) return ''
  if (kind === 'money') return formatMoney(v)
  return String(v)
}

/** Status text → MUI Chip colour (002-spec §8.0). Unknown values stay neutral (REQ-CMP-3). */
export function statusColor(status: unknown, closedValues: string[] = []): ChipProps['color'] {
  const s = String(status ?? '')
  if (!s) return 'default'
  if (s.includes('作废') || s.includes('停业') || s.includes('离职')) return 'default'
  if (closedValues.some((c) => s.includes(c)) || /完成|结案|已收款|已付|已到货|验收合格|核销/.test(s)) return 'success'
  if (/待|审批|审核|未|滞留|退回/.test(s)) return 'warning'
  if (/进行|跟进|递出|报出/.test(s)) return 'info'
  return 'default'
}

export const isVoid = (status: unknown) => String(status ?? '').includes('作废')

export function formatBytes(n: number | null): string {
  if (n === null) return ''
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(0)} KB`
  return `${(n / 1024 / 1024).toFixed(1)} MB`
}
