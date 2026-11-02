import { useMemo, useState } from 'react'
import {
  Alert, Autocomplete, Button, Dialog, DialogActions, DialogContent, DialogTitle, Grid, MenuItem, TextField, Typography,
} from '@mui/material'
import { api, ApiError } from '../api/client'
import type { EditField, ModuleMeta, Row, SaveResult } from '../api/types'
import { useLookup, useStaff } from '../api/useLookups'
import { useNotify } from './Notify'

interface Props {
  module: ModuleMeta
  /** Edit mode when set: current record + its ETag. */
  record?: Row
  eTag?: string
  /** Create-from-parent (REQ-EDIT-2), e.g. a 结算 from a 合同. */
  parent?: { module: string; id: number; title: string }
  onClose: () => void
  onSaved: (result: SaveResult) => void
  /** Called when the record changed since it was opened (409); the page reloads it. */
  onConflict?: () => void
}

const toText = (v: unknown) => (v === null || v === undefined ? '' : String(v))

/** Generic create/edit form built from the legacy edit page's fields (REQ-EDIT-1/5/6). */
export default function RecordForm({ module: m, record, eTag, parent, onClose, onSaved, onConflict }: Props) {
  const { notify } = useNotify()
  const editing = !!record
  const initial = useMemo(
    () => Object.fromEntries(m.editFields.map((f) => [f.name, toText(record?.[f.name])])) as Record<string, string>,
    [m.editFields, record],
  )
  const [values, setValues] = useState<Record<string, string>>(initial)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [conflict, setConflict] = useState(false)
  const [busy, setBusy] = useState(false)

  const set = (name: string, v: string) => {
    setValues((cur) => ({ ...cur, [name]: v }))
    if (errors[name]) setErrors((cur) => Object.fromEntries(Object.entries(cur).filter(([k]) => k !== name)))
  }

  async function submit() {
    const missing = m.editFields.filter((f) => f.required && !values[f.name]?.trim())
    if (missing.length) {
      setErrors(Object.fromEntries(missing.map((f) => [f.name, `${f.label}为必填项`])))
      return
    }
    setBusy(true)
    setError(null)
    try {
      // Edit: send only what changed, so a parallel edit of another field in PHP is not overwritten.
      const payload = editing ? Object.fromEntries(Object.entries(values).filter(([k, v]) => v !== initial[k])) : values
      if (editing && Object.keys(payload).length === 0) {
        onClose()
        return
      }
      const res = editing
        ? await api<SaveResult>(`/modules/${m.key}/${record!.id}`, { method: 'PUT', body: { values: payload }, headers: { 'If-Match': eTag ?? '' } })
        : await api<SaveResult>(`/modules/${m.key}`, {
            method: 'POST',
            body: { values: payload, parentModule: parent?.module ?? null, parentId: parent?.id ?? null },
          })
      notify(editing ? '已保存' : '已新增')
      onSaved(res)
    } catch (e) {
      if (e instanceof ApiError && e.code === 'CONCURRENT_EDIT') setConflict(true)
      else if (e instanceof ApiError && e.code === 'VALIDATION' && e.data && typeof e.data === 'object') {
        setErrors(e.data as Record<string, string>)
        setError(e.message)
      } else setError(e instanceof ApiError ? e.message : '保存失败')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog open onClose={busy ? undefined : onClose} maxWidth="md" fullWidth>
      <DialogTitle>
        {editing ? `编辑${m.title}` : `新增${m.title}`}
        {parent && <Typography variant="body2" color="text.secondary">关联到：{parent.title}</Typography>}
      </DialogTitle>
      <DialogContent dividers>
        {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
        {m.editFields.length === 0 ? (
          <Alert severity="info">该模块的表单将在后续阶段提供。</Alert>
        ) : (
          <Grid container spacing={2}>
            {m.editFields.map((f) => (
              <Grid key={f.name} size={{ xs: 12, sm: f.input === 'textarea' ? 12 : 6 }}>
                <FieldInput field={f} value={values[f.name] ?? ''} error={errors[f.name]} onChange={(v) => set(f.name, v)} />
              </Grid>
            ))}
          </Grid>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>取消</Button>
        <Button variant="contained" onClick={submit} disabled={busy || m.editFields.length === 0}>{editing ? '保存' : '新增'}</Button>
      </DialogActions>

      <Dialog open={conflict} onClose={() => setConflict(false)}>
        <DialogTitle>记录已被其他人修改</DialogTitle>
        <DialogContent>
          <Typography variant="body2">打开后，这条记录已在新系统或旧系统中被修改。为避免覆盖他人的修改，请重新载入最新内容后再编辑。</Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConflict(false)}>返回</Button>
          <Button variant="contained" onClick={() => { setConflict(false); onConflict?.(); onClose() }}>重新载入</Button>
        </DialogActions>
      </Dialog>
    </Dialog>
  )
}

function FieldInput({ field: f, value, error, onChange }: { field: EditField; value: string; error?: string; onChange: (v: string) => void }) {
  const common = { label: f.label, required: f.required, error: !!error, helperText: error, value }
  if (f.input === 'staff') return <StaffInput {...common} onChange={onChange} />
  if (f.input === 'select') return <LookupSelect {...common} category={f.lookup!} onChange={onChange} />
  if (f.input === 'date')
    return <TextField {...common} type="date" onChange={(e) => onChange(e.target.value)} slotProps={{ inputLabel: { shrink: true } }} />
  if (f.input === 'money')
    return (
      <TextField
        {...common}
        onChange={(e) => onChange(e.target.value)}
        slotProps={{ htmlInput: { inputMode: 'decimal', style: { textAlign: 'right', fontVariantNumeric: 'tabular-nums' } } }}
      />
    )
  if (f.input === 'textarea') return <TextField {...common} multiline minRows={3} onChange={(e) => onChange(e.target.value)} />
  return <TextField {...common} onChange={(e) => onChange(e.target.value)} />
}

type CommonProps = { label: string; required: boolean; error: boolean; helperText?: string; value: string; onChange: (v: string) => void }

function LookupSelect({ category, value, onChange, ...rest }: CommonProps & { category: string }) {
  const options = useLookup(category)
  const list = options.data ?? []
  // Keep a legacy value that is no longer in the dropdown visible instead of silently blanking it (REQ-CMP-3).
  const all = value && !list.includes(value) ? [value, ...list] : list
  return (
    <TextField select {...rest} value={value} onChange={(e) => onChange(e.target.value)}>
      <MenuItem value=""><em>（空）</em></MenuItem>
      {all.map((o) => <MenuItem key={o} value={o}>{o}</MenuItem>)}
    </TextField>
  )
}

function StaffInput({ value, onChange, ...rest }: CommonProps) {
  const staff = useStaff()
  return (
    <Autocomplete
      freeSolo
      options={staff.data ?? []}
      value={value}
      onChange={(_, v) => onChange(v ?? '')}
      onInputChange={(_, v) => onChange(v)}
      renderInput={(params) => <TextField {...params} {...rest} />}
    />
  )
}
