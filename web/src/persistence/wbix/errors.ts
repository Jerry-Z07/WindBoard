/**
 * WBIX 读写错误与容忍告警。
 *
 * 分层原则（对齐桌面 `WbixWorkspaceSerializer`）：
 * - **致命**：抛 `WbixError`，整个包读/写失败；
 * - **容忍**：收集 `WbixWarning`，跳过单条数据继续，语义与桌面 `AppLog.Warn` + 跳过一致。
 */

/** 致命错误码。 */
export type WbixErrorCode =
  /** zip 容器本身无法解析。 */
  | 'zip-read-failed'
  /** JSON 文本语法错误（注释/尾随逗号已按桌面选项容忍后仍失败）。 */
  | 'json-parse-failed'
  /** 缺少 `manifest.json` 条目。 */
  | 'missing-manifest'
  /** manifest 结构非法（如 `pages` 缺失/类型错误、Guid 非法）。 */
  | 'manifest-parse-failed'
  /** `format` 字段不是 `wbix`。 */
  | 'format-mismatch'
  /** `version` 超出支持范围（<=0 或 > 当前版本）。 */
  | 'version-not-supported'
  /** 页条目缺失，或页路径不安全（含 `..`、不以 `pages/` 开头）。 */
  | 'missing-page-file'
  /** 页 payload 结构非法。 */
  | 'page-parse-failed'
  /** 笔迹条目结构非法（如 v3 包装里 `stroke`/`shape` 为 null、缺少 `points`）。 */
  | 'ink-item-invalid'
  /** 写侧：数值不是有限 float32（NaN / ±Infinity）。 */
  | 'number-not-finite'
  /** 写侧：字段取值非法（空 Guid、空资源路径、非整数等）。 */
  | 'invalid-value'

/** WBIX 致命错误。 */
export class WbixError extends Error {
  readonly code: WbixErrorCode

  constructor(code: WbixErrorCode, message: string) {
    super(message)
    this.name = 'WbixError'
    this.code = code
  }
}

/** 容忍告警码（每条对应桌面侧一处"跳过并 Warn"的分支）。 */
export type WbixWarningCode =
  /** `strokes` 数组里的 null 条目（桌面 `InkItemSnapshot` 为 null，跳过）。 */
  | 'ink-item-null'
  /** 未知笔迹 kind（桌面 `BoardInkItemCodec.ToItem` 的 default 分支）。 */
  | 'ink-item-unknown-kind'
  /** v3 文件里出现 v1/v2 扁平笔迹条目（桌面每个文件只告警一次）。 */
  | 'ink-item-legacy-flat'
  /** `elements` 数组里的 null 条目（桌面访问 `e.Type` 触发异常后跳过）。 */
  | 'element-null'
  /** `type` 缺失或空白（桌面直接跳过）。 */
  | 'element-type-empty'
  /** 未知元素类型（桌面 `TryParseElement` 返回 null）。 */
  | 'element-unknown-type'
  /** 单元素解析异常（桌面 catch 后 Warn 跳过）。 */
  | 'element-parse-failed'
  /** 资源路径不安全（非 `assets/` 前缀、含 `..`）。 */
  | 'resource-path-unsafe'
  /** 资源条目在 zip 里找不到（大小写敏感精确匹配）。 */
  | 'resource-missing'
  /** 单资源超过 32 MiB。 */
  | 'resource-too-large'
  /** 累计提取量超过 256 MiB。 */
  | 'resource-total-limit'

/** 容忍告警。 */
export interface WbixWarning {
  code: WbixWarningCode
  detail: string
}
