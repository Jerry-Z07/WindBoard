import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './styles/tokens.css'
import './index.css'
import './styles/shell.css'
import './styles/chrome.css'
import './styles/dock.css'
import './styles/settings.css'
import './styles/settings-search.css'
import './styles/dialogs.css'
import './styles/overlay.css'
import App from './App.tsx'
import { attachLaunchQueue } from './platform/file-handlers.ts'
import { registerServiceWorker } from './platform/pwa.ts'

// 系统「用 WindBoard 打开 .wbix」的入口：必须在渲染前挂上，否则早期派发的文件会丢
attachLaunchQueue()
// 离线与安装能力；dev 环境下函数内部直接返回
registerServiceWorker()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
