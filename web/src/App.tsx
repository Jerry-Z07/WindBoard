import './App.css'

/**
 * P0 阶段的占位页。
 *
 * 说明：本阶段（P0）只交付工程骨架、JSON/f32 格式层与 WBIX 读写，画布渲染在 P1、
 * 界面外壳在 P3 实现。这里刻意不放任何演示内容，避免后续被误认为"已实现的 UI"。
 */
function App() {
  return (
    <main className="placeholder">
      <h1>WindBoard Web</h1>
      <p>P0 骨架已就绪：WBIX 读写、f32 与 JSON 形态对齐桌面端。</p>
      <p className="hint">画布与界面将在后续阶段实现。</p>
    </main>
  )
}

export default App
