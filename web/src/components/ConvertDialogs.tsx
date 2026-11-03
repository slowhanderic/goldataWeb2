import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import {
  Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, Radio, RadioGroup, Stack, Table, TableBody, TableCell,
  TableRow, TextField, Typography,
} from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { api, ApiError } from '../api/client'
import type { ListResult } from '../api/types'
import { formatMoney } from '../utils/format'
import StatusChip from './StatusChip'
import { useNotify } from './Notify'

/** 采购计划/请购明细 → 订单明细 (REQ-LINK-3): pick an order, copy the selected lines (product, unit, standard, brand, …; no qty, as legacy). */
export function CopyToOrderDialog({ source, itemIds, onClose }: { source: 'buy-plans' | 'buy-requests'; itemIds: number[]; onClose: () => void }) {
  const navigate = useNavigate()
  const { notify } = useNotify()
  const [search, setSearch] = useState('')
  const [q, setQ] = useState('')
  const [orderId, setOrderId] = useState<number | null>(null)
  const [variant, setVariant] = useState<'default' | 'report'>('default')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  useEffect(() => {
    const h = setTimeout(() => setQ(search), 300)
    return () => clearTimeout(h)
  }, [search])
  const orders = useQuery({
    queryKey: ['convert-orders', q],
    queryFn: () => api<ListResult>('/modules/buy-orders', { query: { q, pageSize: 20 } }),
  })

  async function submit() {
    setBusy(true)
    setError(null)
    try {
      const path = source === 'buy-plans' ? 'plan-items-to-order' : 'request-items-to-order'
      const r = await api<{ copied: number }>(`/conversions/${path}`, { method: 'POST', body: { itemIds, buyOrderId: orderId, variant } })
      notify(`已复制 ${r.copied} 行到订单`)
      navigate(`/m/buy-orders/${orderId}`)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : '复制失败')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog open onClose={busy ? undefined : onClose} maxWidth="md" fullWidth>
      <DialogTitle>复制 {itemIds.length} 行明细到订单</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={1.5}>
          {error && <Alert severity="error">{error}</Alert>}
          <Typography variant="body2" color="text.secondary">
            复制产品、单位、标准、品牌{source === 'buy-plans' ? '、成本询价单价' : ''}和备注；数量需在订单中填写（与旧系统一致）。
          </Typography>
          {source === 'buy-requests' && (
            <RadioGroup row value={variant} onChange={(e) => setVariant(e.target.value as 'default' | 'report')}>
              <FormControlLabel value="default" control={<Radio size="small" />} label="复制需求限期、优先等级、预计到货" />
              <FormControlLabel value="report" control={<Radio size="small" />} label="按请购报表：复制建议单价" />
            </RadioGroup>
          )}
          <TextField autoFocus placeholder="搜索订单（编号、供应商…）" value={search} onChange={(e) => setSearch(e.target.value)} />
          <Table size="small">
            <TableBody>
              {(orders.data?.rows ?? []).map((o) => (
                <TableRow key={o.id} hover selected={orderId === o.id} onClick={() => setOrderId(o.id)} sx={{ cursor: 'pointer' }}>
                  <TableCell padding="checkbox"><Radio size="small" checked={orderId === o.id} /></TableCell>
                  <TableCell>{o._title}</TableCell>
                  <TableCell>{String(o.order_date ?? '')}</TableCell>
                  <TableCell><StatusChip value={o.status} /></TableCell>
                  <TableCell align="right">{formatMoney(o.amount)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>取消</Button>
        <Button variant="contained" disabled={!orderId || busy} onClick={submit}>复制到订单</Button>
      </DialogActions>
    </Dialog>
  )
}

/** 订单 → 收货单: new receipt linked to the order, with all order lines copied (insert.php caller buy_orders). */
export function ReceiptFromOrderDialog({ orderId, onClose }: { orderId: number; onClose: () => void }) {
  const navigate = useNavigate()
  const { notify } = useNotify()
  const [f, setF] = useState({ arrived_date: new Date().toISOString().slice(0, 10), tag: '' })
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  async function submit() {
    setBusy(true)
    setError(null)
    try {
      const r = await api<{ id: number }>('/conversions/order-to-receipt', { method: 'POST', body: { buyOrderId: orderId, values: f } })
      notify('收货单已生成，请填写进库目的地')
      navigate(`/m/buy-receipts/${r.id}`)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : '生成失败')
    } finally {
      setBusy(false)
    }
  }
  return (
    <Dialog open onClose={busy ? undefined : onClose} maxWidth="xs" fullWidth>
      <DialogTitle>生成收货单</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error">{error}</Alert>}
          <Typography variant="body2" color="text.secondary">新收货单会关联本订单，并复制订单的全部明细（数量、单价、合价）。</Typography>
          <TextField type="date" label="到货日期" value={f.arrived_date} onChange={(e) => setF({ ...f, arrived_date: e.target.value })} slotProps={{ inputLabel: { shrink: true } }} />
          <TextField label="编号/标签" value={f.tag} onChange={(e) => setF({ ...f, tag: e.target.value })} />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>取消</Button>
        <Button variant="contained" onClick={submit} disabled={busy}>生成</Button>
      </DialogActions>
    </Dialog>
  )
}
