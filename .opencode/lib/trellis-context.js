/**
 * Trellis Context Manager
 *
 * Utility class for OpenCode plugins providing file reading,
 * JSONL parsing, and context building capabilities.
 */

import { existsSync, readFileSync, appendFileSync, readdirSync } from "fs"
import { isAbsolute, join } from "path"
import { platform } from "os"
import { execSync } from "child_process"

const PYTHON_CMD = platform() === "win32" ? "python" : "python3"
// Debug logging
const DEBUG_LOG = "/tmp/trellis-plugin-debug.log"

function debugLog(prefix, ...args) {
  const timestamp = new Date().toISOString()
  const msg = `[${timestamp}] [${prefix}] ${args.map(a => typeof a === "object" ? JSON.stringify(a) : a).join(" ")}\n`
  try {
    appendFileSync(DEBUG_LOG, msg)
  } catch {
    // ignore
  }
}

/**
 * Trellis Context Manager
 */
export class TrellisContext {
  constructor(directory) {
    this.directory = directory
    debugLog("context", "TrellisContext initialized", { directory })
  }

  // ============================================================
  // Trellis Project Detection
  // ============================================================

  isTrellisProject() {
    return existsSync(join(this.directory, ".trellis"))
  }

  /**
   * Get current task directory from .trellis/.current-task
   */
  getCurrentTask() {
    try {
      const currentTaskPath = join(this.directory, ".trellis", ".current-task")
      if (!existsSync(currentTaskPath)) {
        return null
      }
      const taskRef = readFileSync(currentTaskPath, "utf-8").trim()
      const normalized = this.normalizeTaskRef(taskRef)
      return normalized || null
    } catch {
      return null
    }
  }

  normalizeTaskRef(taskRef) {
    if (!taskRef) {
      return ""
    }

    if (isAbsolute(taskRef)) {
      return taskRef.trim()
    }

    let normalized = taskRef.trim().replace(/\\/g, "/")
    while (normalized.startsWith("./")) {
      normalized = normalized.slice(2)
    }

    if (normalized.startsWith("tasks/")) {
      return `.trellis/${normalized}`
    }

    return normalized
  }

  resolveTaskDir(taskRef) {
    const normalized = this.normalizeTaskRef(taskRef)
    if (!normalized) {
      return null
    }

    if (isAbsolute(normalized)) {
      return normalized
    }

    if (normalized.startsWith(".trellis/")) {
      return join(this.directory, normalized)
    }

    return join(this.directory, ".trellis", "tasks", normalized)
  }

  // ============================================================
  // File Reading Utilities
  // ============================================================

  readFile(filePath) {
    try {
      if (existsSync(filePath)) {
        return readFileSync(filePath, "utf-8")
      }
    } catch {
      // Ignore read errors
    }
    return null
  }

  readProjectFile(relativePath) {
    return this.readFile(join(this.directory, relativePath))
  }

  runScript(scriptPath, cwd = null) {
    try {
      const result = execSync(`${PYTHON_CMD} "${scriptPath}"`, {
        cwd: cwd || this.directory,
        timeout: 10000,
        encoding: "utf-8",
        stdio: ["pipe", "pipe", "pipe"]
      })
      return result || ""
    } catch {
      return ""
    }
  }

  // ============================================================
  // JSONL Reading
  // ============================================================

  readDirectoryMdFiles(dirPath, maxFiles = 20) {
    const results = []
    const fullPath = join(this.directory, dirPath)

    if (!existsSync(fullPath)) {
      return results
    }

    try {
      const files = readdirSync(fullPath)
        .filter(f => f.endsWith(".md"))
        .sort()
        .slice(0, maxFiles)

      for (const filename of files) {
        const filePath = join(dirPath, filename)
        const content = this.readProjectFile(filePath)
        if (content) {
          results.push({ path: filePath, content })
        }
      }
    } catch {
      // Ignore directory read errors
    }

    return results
  }

  /**
   * Read a JSONL file and load referenced files/directories
   * Supports:
   *   {"file": "path/to/file.md", "reason": "..."}
   *   {"file": "path/to/dir/", "type": "directory", "reason": "..."}
   */
  readJsonlWithFiles(jsonlPath) {
    const results = []
    const content = this.readFile(jsonlPath)
    if (!content) return results

    for (const line of content.split("\n")) {
      if (!line.trim()) continue
      try {
        const item = JSON.parse(line)
        const file = item.file || item.path
        const entryType = item.type || "file"

        if (!file) continue

        if (entryType === "directory") {
          const dirEntries = this.readDirectoryMdFiles(file)
          results.push(...dirEntries)
        } else {
          const fullPath = join(this.directory, file)
          const fileContent = this.readFile(fullPath)
          if (fileContent) {
            results.push({ path: file, content: fileContent })
          }
        }
      } catch {
        // Ignore parse errors for individual lines
      }
    }
    return results
  }

  buildContextFromEntries(entries) {
    return entries.map(e => `=== ${e.path} ===\n${e.content}`).join("\n\n")
  }
}

// ============================================================
// Context Collector (for session deduplication)
// ============================================================

class ContextCollector {
  constructor() {
    this.processed = new Set()
  }

  markProcessed(sessionID) {
    this.processed.add(sessionID)
  }

  isProcessed(sessionID) {
    return this.processed.has(sessionID)
  }

  clear(sessionID) {
    this.processed.delete(sessionID)
  }
}

// Singleton instance
export const contextCollector = new ContextCollector()

// Export debug log for plugins
export { debugLog }
