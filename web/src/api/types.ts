// Mirrors the C# DTOs (camelCase JSON). Record rows keep the legacy snake_case column names.

export interface MenuItem { key: string; title: string; generic: boolean }
export interface MenuGroup { group: string; label: string; items: MenuItem[] }

export interface Me {
  id: number
  login: string
  displayName: string
  role: string
  rank: string
  roleIsReadOnly: boolean
  mustChangePassword: boolean
  permissions: string[]
  menu: MenuGroup[]
}

export type FieldKind = 'id' | 'int' | 'text' | 'date' | 'datetime' | 'money' | 'json'
export interface FieldMeta { name: string; label: string; kind: FieldKind }
export interface FileSlot { tag: string; folder: string; label: string }

export interface ModuleMeta {
  key: string
  title: string
  group: string
  groupLabel: string
  titleColumns: string[]
  statusColumn: string | null
  closedValues: string[]
  yearColumn: string | null
  defaultSort: string
  totals: string[]
  workflow: boolean
  hasItems: boolean
  itemsLabel: string | null
  fileSlots: FileSlot[]
  fields: FieldMeta[]
  itemFields: FieldMeta[]
  actions: string[]
  editFields: EditField[]
  statusLookup: string | null
  createFrom: CreateLink[]
  createChildren: CreateLink[]
}

export type EditInput = 'text' | 'textarea' | 'date' | 'money' | 'select' | 'staff'
export interface EditField { name: string; label: string; input: EditInput; lookup: string | null; required: boolean }
export interface CreateLink { module: string; title: string; column: string }
export interface SaveResult { id: number; eTag: string }
export interface LookupItem { id: number; item: string; value: string; data: string; parentId: number; ranking: number }
export interface DependentRef { table: string; column: string; count: number; module: string | null; moduleTitle: string | null }
export interface RelatedRow { id: number; title: string; status: string | null }
export interface RelatedGroup { module: string; title: string; column: string; label: string; total: number; rows: RelatedRow[] }
export interface ParentRef { column: string; label: string; module: string; moduleTitle: string; id: number; title: string }
export interface RelatedResult { parents: ParentRef[]; children: RelatedGroup[] }

export type Row = Record<string, unknown> & { id: number; _title?: string }

export interface ListResult { rows: Row[]; total: number; totals: Record<string, number | null>; page: number; pageSize: number }

export interface RecordDetail { record: Row; title: string; items: Row[]; eTag: string }

export interface FileEntry { id: number; tag: string; slotLabel: string; name: string; path: string; timestamp: string; exists: boolean; size: number | null }
export interface LogEntry { id: number; user: string; tag: string; sqlText: string; updateTime: string }
export interface HistoryEntry { id: number; personFrom: string; personTo: string; statusFrom: string; statusTo: string; comment: string; timestamp: string }

export interface TodoItem {
  historyId: number
  module: string
  moduleTitle: string
  recordId: number
  title: string
  personFrom: string
  personTo: string
  statusTo: string
  comment: string
  timestamp: string
}

export interface SearchHit { module: string; moduleTitle: string; id: number; title: string; status: string | null }

export interface ApiTokenInfo { id: number; name: string | null; scopes: string; expiresAt: string; lastUsedAt: string | null; revokedAt: string | null; createdAt: string }

// ---- admin ----
export interface UserListItem {
  id: number
  login: string
  displayName: string
  roleId: number
  role: string
  roleIsReadOnly: boolean
  rank: string
  status: 'active' | 'disabled' | 'locked'
  mustChangePassword: boolean
  lastLoginAt: string | null
  activeTokens: number
  rowVersion: number
  nameLocked: boolean
}
export interface AppRole { id: number; name: string; description: string | null; isReadOnly: boolean; isSeeded: boolean; sort: number; rowVersion: number; userCount: number }
export interface RankItem { name: string; sort: number; userCount: number }
export interface PermissionDef { code: string; label: string; group: string; module: string | null; action: string | null; isWrite: boolean; isSensitive: boolean }
export interface PermissionSource { permission: string; sources: string[] }
export interface UserOverride { permission: string; allow: boolean }
export interface RoleMatrix { layers: Record<string, string[]>; rowVersion: number }
export interface MatrixDiff { added: string[]; removed: string[]; affectedUsers: string[]; sensitive: boolean }
export interface DeleteCheck { openTodos: { module: string; moduleTitle: string; recordId: number }[]; activeTokens: number; lastLoginAt: string | null; inStaffLookup: boolean }
export interface AdminAuditEntry { id: number; actorName: string; action: string; targetType: string; targetId: number | null; targetName: string | null; beforeJson: string | null; afterJson: string | null; ip: string | null; at: string }

// ---- links (REQ-LINK) ----
export interface LinkTypeInfo {
  code: string; title: string; fromModule: string; toModule: string; otherModule: string; otherTitle: string
  kind: 'share' | 'fk'; shareLabel: string; canEdit: boolean
}
export interface LinkEntry { linkId: number; otherId: number; otherTitle: string; share: number | null }
export interface LinkGroup { type: LinkTypeInfo; direction: 'out' | 'in'; items: LinkEntry[]; allocated: number | null; amount: number | null }
export interface LinkCandidate { id: number; title: string; status: string | null; amount: number | null; allocated: number | null; alreadyLinked: boolean }
