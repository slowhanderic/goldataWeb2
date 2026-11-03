import { useEffect, useMemo, useState } from 'react'
import {
  Alert, Autocomplete, Box, Button, IconButton, MenuItem, Table, TableBody, TableCell, TableHead, TableRow, TextField, Tooltip, Typography,
} from '@mui/material'
import AddIcon from '@mui/icons-material/Add'
import DeleteIcon from '@mui/icons-material/DeleteOutlined'
import { useQuery } from '@tanstack/react-query'
import { api, ApiError } from '../api/client'
import type { ItemFieldMeta, LookupItem, ModuleMeta, Product, Row } from '../api/types'
import { formatMoney } from '../utils/format'
import { amountSx } from '../theme'
import { useNotify } from './Notify'

type Line = { key: string; id: number | null; product: string; values: Record<string, string> }

const toText = (v: unknown) => (v === null || v === undefined ? '' : String(v))
let seq = 0

/** Editable line-item grid (REQ-EDIT-3): product search over the 4-level catalogue, live sub_total, explicit deletes. */
export default function ItemsEditor({ m, recordId, items, eTag, onSaved, onCancel, onConflict }: {
  m: ModuleMeta; recordId: number; items: Row[]; eTag: string; onSaved: () => void; onCancel: () => void; onConflict: () => void
}) {
  const { notify } = useNotify()
  const spec = m.itemEdit!
  const [lines, setLines] = useState<Line[]>(() =>
    items.map((it) => ({
      key: `k${seq++}`, id: Number(it.id), product: toText(it._product),
      values: Object.fromEntries(spec.fields.map((f) => [f.name, toText(it[f.name])])),
    })),
  )
  const [deleted, setDeleted] = useState<number[]>([])
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const destinations = useQuery({
    queryKey: ['lookup-items', '进库目的地'],
    queryFn: () => api<LookupItem[]>(`/lookups/${encodeURIComponent('进库目的地')}`),
    enabled: spec.fields.some((f) => f.kind === 'destination'),
    staleTime: 600_000,
  })

  const qtyField = spec.subTotal ? 'qty' : null
  const priceField = spec.subTotal ? (spec.fields.find((f) => f.kind === 'money')?.name ?? 'unit_price') : null
  const subTotal = (l: Line) => (qtyField && priceField ? (Number(l.values[qtyField]) || 0) * (Number(l.values[priceField]) || 0) : 0)
  const total = useMemo(() => lines.reduce((s, l) => s + subTotal(l), 0), [lines]) // eslint-disable-line react-hooks/exhaustive-deps

  const set = (key: string, patch: Partial<Line> | ((l: Line) => Partial<Line>)) =>
    setLines((ls) => ls.map((l) => (l.key === key ? { ...l, ...(typeof patch === 'function' ? patch(l) : patch) } : l)))

  function addLine() {
    setLines((ls) => [...ls, { key: `k${seq++}`, id: null, product: '', values: Object.fromEntries(spec.fields.map((f) => [f.name, ''])) }])
  }

  function removeLine(l: Line) {
    if (l.id !== null) setDeleted((d) => [...d, l.id!])
    setLines((ls) => ls.filter((x) => x.key !== l.key))
  }

  async function save() {
    setBusy(true)
    setError(null)
    try {
      await api(`/modules/${m.key}/${recordId}/items`, {
        method: 'PUT',
        headers: { 'If-Match': eTag },
        body: { items: lines.map((l) => ({ id: l.id, values: l.values })), deletedIds: deleted },
      })
      notify('明细已保存')
      onSaved()
    } catch (e) {
      if (e instanceof ApiError && e.code === 'CONCURRENT_EDIT') {
        setError('该记录或明细已被其他人修改（新系统或旧系统）。请取消后重新载入。')
        onConflict()
      } else if (e instanceof ApiError && e.data && typeof e.data === 'object') {
        setError(`${e.message}：${Object.values(e.data as Record<string, string>).join('；')}`)
      } else setError(e instanceof ApiError ? e.message : '保存失败')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Box>
      {error && <Alert severity="error" sx={{ mb: 1 }}>{error}</Alert>}
      <Box sx={{ overflowX: 'auto' }}>
        <Table size="small" sx={{ '& td, & th': { px: 0.75 } }}>
          <TableHead>
            <TableRow>
              <TableCell width={36}>#</TableCell>
              {spec.fields.map((f) => <TableCell key={f.name} sx={{ minWidth: f.kind === 'product' ? 320 : f.kind === 'text' ? 120 : 90 }}>{f.label}</TableCell>)}
              {spec.subTotal && <TableCell align="right" sx={{ minWidth: 100 }}>合价</TableCell>}
              <TableCell width={40} />
            </TableRow>
          </TableHead>
          <TableBody>
            {lines.map((l, i) => (
              <TableRow key={l.key}>
                <TableCell>{i + 1}</TableCell>
                {spec.fields.map((f) => (
                  <TableCell key={f.name}>
                    <Cell field={f} line={l} destinations={destinations.data ?? []}
                      onChange={(v) => set(l.key, (cur) => ({ values: { ...cur.values, [f.name]: v } }))}
                      onProduct={(p) => set(l.key, (cur) => ({
                        product: p?.path ?? '',
                        values: { ...cur.values, product_id: p ? String(p.id) : '', unit: p && !cur.values.unit ? p.unit : cur.values.unit ?? '' },
                      }))} />
                  </TableCell>
                ))}
                {spec.subTotal && <TableCell align="right" sx={amountSx}>{formatMoney(subTotal(l))}</TableCell>}
                <TableCell>
                  <Tooltip title="删除此行"><IconButton onClick={() => removeLine(l)}><DeleteIcon /></IconButton></Tooltip>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Box>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mt: 1, flexWrap: 'wrap' }}>
        <Button startIcon={<AddIcon />} onClick={addLine}>添加一行</Button>
        <Box sx={{ flexGrow: 1 }} />
        {spec.subTotal && (
          <Typography variant="body2" sx={amountSx}>
            合计：<b>{formatMoney(total)}</b>{spec.headerTotal ? '（保存后更新为单据金额）' : ''}
          </Typography>
        )}
        <Button onClick={onCancel} disabled={busy}>取消</Button>
        <Button variant="contained" onClick={save} disabled={busy}>保存明细</Button>
      </Box>
    </Box>
  )
}

function Cell({ field: f, line, destinations, onChange, onProduct }: {
  field: ItemFieldMeta; line: Line; destinations: LookupItem[]; onChange: (v: string) => void; onProduct: (p: Product | null) => void
}) {
  const v = line.values[f.name] ?? ''
  if (f.kind === 'product') return <ProductPicker value={line.product} productId={v} onChange={onProduct} />
  if (f.kind === 'destination')
    return (
      <TextField select value={v} onChange={(e) => onChange(e.target.value)} sx={{ minWidth: 150 }}>
        {destinations.map((d) => <MenuItem key={d.id} value={String(d.id)}>{d.item}</MenuItem>)}
      </TextField>
    )
  if (f.kind === 'date') return <TextField type="date" value={v === '0000-00-00' ? '' : v} onChange={(e) => onChange(e.target.value)} />
  if (f.kind === 'number' || f.kind === 'money' || f.kind === 'int')
    return <TextField value={v} onChange={(e) => onChange(e.target.value)} slotProps={{ htmlInput: { inputMode: 'decimal', style: { textAlign: 'right' } } }} />
  return <TextField value={v} onChange={(e) => onChange(e.target.value)} />
}

/** Search the catalogue by any words of 类别 / 细类 / 品名 / 型号 (replaces dropdown/ajax/search-item-name-ep.php). */
function ProductPicker({ value, productId, onChange }: { value: string; productId: string; onChange: (p: Product | null) => void }) {
  const [input, setInput] = useState(value)
  const [q, setQ] = useState('')
  useEffect(() => {
    const h = setTimeout(() => setQ(input === value ? '' : input), 250)
    return () => clearTimeout(h)
  }, [input, value])
  const results = useQuery({
    queryKey: ['catalogue-search', q],
    queryFn: () => api<Product[]>('/catalogue/search', { query: { q, limit: 30 } }),
    enabled: q.trim().length >= 1,
    staleTime: 60_000,
  })
  const current: Product | null = productId ? { id: Number(productId), cat: '', subCat: '', item: '', type: '', unit: '', path: value } : null
  return (
    <Autocomplete<Product, false, false, false>
      size="small"
      options={results.data ?? []}
      value={current}
      filterOptions={(x) => x}
      getOptionLabel={(p) => p.path}
      isOptionEqualToValue={(a, b) => a.id === b.id}
      loading={results.isFetching}
      noOptionsText={q ? '没有找到' : '输入类别、品名或型号搜索'}
      inputValue={input}
      onInputChange={(_, v) => setInput(v)}
      onChange={(_, p) => onChange(p)}
      renderOption={(props, p) => (
        <li {...props} key={p.id}>
          <Box>
            <Typography variant="body2">{p.item} / <b>{p.type}</b></Typography>
            <Typography variant="caption" color="text.secondary">{p.cat} / {p.subCat}{p.unit ? ` · ${p.unit}` : ''}</Typography>
          </Box>
        </li>
      )}
      renderInput={(params) => <TextField {...params} placeholder="搜索产品…" error={!productId} />}
    />
  )
}
