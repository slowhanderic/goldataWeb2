import { useState } from 'react'
import { Alert, Autocomplete, Button, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Stack, TextField } from '@mui/material'
import { api, ApiError } from '../api/client'
import type { ModuleMeta } from '../api/types'
import { useLookup, useStaff } from '../api/useLookups'
import { useNotify } from './Notify'

/** 转交 (REQ-WF-1/2): set the new status and the person it waits on, with a required comment — like legacy show_history(). */
export default function HandoverDialog({ module: m, id, currentStatus, onClose, onDone }: {
  module: ModuleMeta; id: number; currentStatus: string; onClose: () => void; onDone: () => void
}) {
  const { notify } = useNotify()
  const statuses = useLookup(m.statusLookup)
  const staff = useStaff()
  const [f, setF] = useState({ statusTo: '', personTo: '', comment: '' })
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit() {
    const e: Record<string, string> = {}
    if (!f.statusTo) e.statusTo = '必填项'
    if (!f.personTo) e.personTo = '必填项'
    if (!f.comment.trim()) e.comment = '必填项'
    setErrors(e)
    if (Object.keys(e).length) return
    setBusy(true)
    try {
      await api(`/modules/${m.key}/${id}/handover`, { method: 'POST', body: f })
      notify(`已转交给 ${f.personTo}`)
      onDone()
    } catch (err) {
      if (err instanceof ApiError && err.data && typeof err.data === 'object') setErrors(err.data as Record<string, string>)
      setError(err instanceof ApiError ? err.message : '转交失败')
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog open onClose={busy ? undefined : onClose} maxWidth="sm" fullWidth>
      <DialogTitle>转交</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          {error && <Alert severity="error">{error}</Alert>}
          <TextField label="当前状态" value={currentStatus || '（空）'} disabled />
          <TextField select label="状态更新为" required value={f.statusTo} error={!!errors.statusTo} helperText={errors.statusTo}
            onChange={(e) => setF({ ...f, statusTo: e.target.value })}>
            {(statuses.data ?? []).map((s) => <MenuItem key={s} value={s}>{s}</MenuItem>)}
          </TextField>
          <Autocomplete
            options={staff.data ?? []}
            value={f.personTo || null}
            onChange={(_, v) => setF({ ...f, personTo: v ?? '' })}
            renderInput={(p) => <TextField {...p} label="转交给" required error={!!errors.personTo} helperText={errors.personTo} />}
          />
          <TextField label="备注" required multiline minRows={3} value={f.comment} error={!!errors.comment} helperText={errors.comment}
            onChange={(e) => setF({ ...f, comment: e.target.value })} />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>取消</Button>
        <Button variant="contained" onClick={submit} disabled={busy}>转交</Button>
      </DialogActions>
    </Dialog>
  )
}
