// Fetch wrapper: same-origin cookie session, antiforgery header on unsafe requests, RFC 9457 errors.

export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly data: unknown
  constructor(status: number, code: string, title: string, data?: unknown) {
    super(title)
    this.status = status
    this.code = code
    this.data = data
  }
}

const BASE = '/api/v1'
let csrfToken: string | null = null

export function setCsrfToken(token: string | null) {
  csrfToken = token
}

async function csrf(): Promise<string> {
  if (!csrfToken) {
    const res = await fetch(`${BASE}/auth/csrf`, { credentials: 'same-origin' })
    csrfToken = ((await res.json()) as { token: string }).token
  }
  return csrfToken
}

type Query = Record<string, string | number | boolean | undefined | null>

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE'
  body?: unknown
  form?: FormData
  query?: Query
  headers?: Record<string, string>
}

export function buildUrl(path: string, query?: Query): string {
  const qs = new URLSearchParams()
  for (const [k, v] of Object.entries(query ?? {})) {
    if (v !== undefined && v !== null && v !== '') qs.set(k, String(v))
  }
  const s = qs.toString()
  return `${BASE}${path}${s ? `?${s}` : ''}`
}

export async function api<T>(path: string, opts: RequestOptions = {}, retried = false): Promise<T> {
  const method = opts.method ?? 'GET'
  const headers: Record<string, string> = { Accept: 'application/json', ...opts.headers }
  if (method !== 'GET') headers['X-XSRF-TOKEN'] = await csrf()
  let body: BodyInit | undefined
  if (opts.form) body = opts.form
  else if (opts.body !== undefined) {
    headers['Content-Type'] = 'application/json'
    body = JSON.stringify(opts.body)
  }

  const res = await fetch(buildUrl(path, opts.query), { method, headers, body, credentials: 'same-origin' })
  if (res.status === 204) return undefined as T
  const isJson = (res.headers.get('content-type') ?? '').includes('json')
  const payload = isJson ? await res.json() : undefined

  if (!res.ok) {
    const code = (payload?.code as string) ?? (res.status === 401 ? 'UNAUTHENTICATED' : `HTTP_${res.status}`)
    if (code === 'CSRF' && !retried) {
      csrfToken = null // token rotated (login, password change): fetch a new one and retry once
      return api<T>(path, opts, true)
    }
    const title = (payload?.title as string) ?? (res.status === 401 ? '请先登入' : res.status === 429 ? '操作太频繁，请稍后再试' : '请求失败')
    throw new ApiError(res.status, code, title, payload?.data)
  }
  return payload as T
}

/** Upload with progress (fetch has no upload progress). */
export function upload<T>(path: string, form: FormData, query: Query, onProgress?: (pct: number) => void): Promise<T> {
  return csrf().then(
    (token) =>
      new Promise<T>((resolve, reject) => {
        const xhr = new XMLHttpRequest()
        xhr.open('POST', buildUrl(path, query))
        xhr.setRequestHeader('X-XSRF-TOKEN', token)
        xhr.setRequestHeader('Accept', 'application/json')
        xhr.upload.onprogress = (e) => e.lengthComputable && onProgress?.(Math.round((e.loaded / e.total) * 100))
        xhr.onload = () => {
          let payload: { code?: string; title?: string; data?: unknown } | undefined
          try {
            payload = JSON.parse(xhr.responseText)
          } catch {
            payload = undefined
          }
          if (xhr.status >= 200 && xhr.status < 300) resolve(payload as T)
          else
            reject(
              new ApiError(xhr.status, payload?.code ?? `HTTP_${xhr.status}`, payload?.title ?? (xhr.status === 413 ? '文件太大' : '上传失败'), payload?.data),
            )
        }
        xhr.onerror = () => reject(new ApiError(0, 'NETWORK', '网络错误，上传失败'))
        xhr.send(form)
      }),
  )
}

export const fileUrl = (fileId: number, download = false) => `${BASE}/files/${fileId}${download ? '?download=true' : ''}`
