import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// 说明：
// - 本配置同时承载 Vite 构建与 Vitest；P0 阶段没有 UI，测试跑在 Node 环境；
// - 用 `vitest/config` 的 defineConfig，才能在 `test` 字段拿到类型支持。
export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'node',
    include: ['tests/**/*.test.ts'],
    reporters: ['default'],
  },
})
