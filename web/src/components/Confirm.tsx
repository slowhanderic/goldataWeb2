import { useState, type ReactNode } from 'react'
import { Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField } from '@mui/material'

interface Props {
  open: boolean
  title: string
  children?: ReactNode
  confirmText?: string
  danger?: boolean
  /** When set, the user must type this text to enable the button (e.g. the login for a delete). */
  typeToConfirm?: string
  busy?: boolean
  onConfirm: () => void
  onClose: () => void
}

export default function Confirm({ open, title, children, confirmText = '确定', danger, typeToConfirm, busy, onConfirm, onClose }: Props) {
  const [typed, setTyped] = useState('')
  const ok = !typeToConfirm || typed === typeToConfirm
  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        {children}
        {typeToConfirm && (
          <TextField sx={{ mt: 2 }} label={`请输入「${typeToConfirm}」确认`} value={typed} onChange={(e) => setTyped(e.target.value)} autoFocus />
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>取消</Button>
        <Button variant="contained" color={danger ? 'error' : 'primary'} disabled={!ok || busy} onClick={onConfirm}>
          {confirmText}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
