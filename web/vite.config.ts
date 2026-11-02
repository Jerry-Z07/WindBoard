import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// 说明：
// - 本配置同时承载 Vite 构建与 Vitest；P0 阶段没有 UI，测试跑在 Node 环境；
// - 用 `vitest/config` 的 defineConfig，才能在 `test` 字段拿到类型支持。
//
// 打包拆分的依据（Vite 8 已切到 Rolldown）：
// - `build.rollupOptions.output.manualChunks` 在 Vite 8 里不再支持（对象形式移除、函数形式废弃），
//   官方迁移说明指向 Rolldown 的 `output.codeSplitting`；
//   见 <https://vite.dev/guide/migration> 与 <https://rolldown.rs/in-depth/manual-code-splitting>；
// - 分组只为两件事：① 三方库与业务代码分开缓存（改业务代码不会让用户重下 React/Fluent）；
//   ② 把单包 780 kB 拆成可并行下载的几块，消掉 Vite 的 >500 kB 提示。
//   静态 import 的 chunk 仍会被 Vite 注入 `modulepreload`，首屏请求数增加但串行等待不变长。
export default defineConfig({
  plugins: [react()],
  build: {
    rolldownOptions: {
      output: {
        codeSplitting: {
          groups: [
            // React 运行时：几乎不随业务改动变化，单独成块命中率最高
            { name: 'react', test: /node_modules[\\/](?:react|react-dom|scheduler)[\\/]/ },
            // Fluent UI 组件库（体积最大的一块）；图标包单独再拆，避免改一个图标就整包失效
            { name: 'fluent', test: /node_modules[\\/]@fluentui[\\/](?!react-icons[\\/])/ },
            { name: 'fluent-icons', test: /node_modules[\\/]@fluentui[\\/]react-icons[\\/]/ },
            // 注：`fflate`（WBIX 的 zip 读写）只被导出/导入链路引用，体积远小于上面两组，
            // 单独成组只会让首屏多一个请求，因此留在主包里不单列。
          ],
        },
      },
    },
  },
  test: {
    /*
     * 默认 node 环境（纯函数测试占绝大多数）；组件级冒烟测试在文件头用
     * `/** @vitest-environment jsdom *\/` 单独切换环境，避免句柄泄漏与无谓开销。
     */
    environment: 'node',
    include: ['tests/**/*.test.ts', 'tests/**/*.test.tsx'],
    reporters: ['default'],
  },
})
