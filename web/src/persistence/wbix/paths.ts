/**
 * WBIX zip 路径规则（逐条对齐桌面 `WbixWorkspaceSerializer.ResourcesAndPaths.cs`）。
 */

/**
 * 归一化 zip 条目路径：反斜杠 → 正斜杠，并去掉前导 `/`。
 *
 * 注意：桌面侧 `GetEntry` 是**大小写敏感**的精确匹配，归一化只处理分隔符与前导斜杠。
 */
export function normalizeZipPath(path: string): string {
  return path.replace(/\\/g, '/').replace(/^\/+/, '')
}

/**
 * 判断归一化后的路径是否安全（对齐 IsSafeZipPath）。规则：非空白；以 requiredPrefix 开头（忽略大小写）；不含 ..；不以 / 或 \ 开头。
 */
export function isSafeZipPath(path: string, requiredPrefix = ''): boolean {
  if (path.trim().length === 0) {
    return false
  }

  if (requiredPrefix.length > 0 && !path.toLowerCase().startsWith(requiredPrefix.toLowerCase())) {
    return false
  }

  if (path.includes('..')) {
    return false
  }

  return !path.startsWith('/') && !path.startsWith('\\')
}

/**
 * 取路径的最后一段文件名（对齐 `Path.GetFileName` 的 Windows 语义）。
 *
 * 说明：桌面在 Windows 上同时把 `/` 与 `\` 视为分隔符；以分隔符结尾时返回空串。
 */
export function fileNameOf(path: string): string {
  const normalized = path.replace(/\\/g, '/')
  const lastSlash = normalized.lastIndexOf('/')
  return lastSlash < 0 ? normalized : normalized.slice(lastSlash + 1)
}
