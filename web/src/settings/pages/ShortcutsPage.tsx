/**
 * 设置 → 快捷键（桌面 `WindBoard/Features/Shortcuts/`，设置页在
 * `WindBoard/Settings/Pages/` 之外，由 `SettingsWindow.xaml` 的导航项承载）。
 *
 * 桌面只有撤销/重做两个可改绑槽位（`KeyboardShortcutsDefaults.cs:8-9`）；
 * 校验规则与桌面一致（`KeyboardShortcutGesture.cs:125-134`）：
 * 必须含 Ctrl 或 Alt、按键不能是修饰键、空字符串表示禁用。
 * 解析/规范化直接复用 `interaction/shortcuts.ts`，本页不重复实现。
 * 鼠标手势是只读说明（桌面同样不可修改，见 `03-ui-inventory.md` §5）。
 */

import { Button, Input, Switch } from '@fluentui/react-components'
import { useState, type ReactNode } from 'react'
import { useSettings, useSettingsService } from '../../app/hooks.ts'
import { useI18n } from '../../i18n/context.ts'
import type { MessageKey } from '../../i18n/locales/zh-CN.generated.ts'
import {
  canonicalShortcutBinding,
  isValidShortcutBinding,
  type ShortcutId,
} from '../../interaction/shortcuts.ts'
import { SETTINGS_ANCHORS } from '../search-targets.ts'
import { SettingsCard, SettingsSection } from './SettingsCard.tsx'

/** 只读的鼠标组合快捷键的文案 key（桌面 `Settings_Shortcuts_Mouse_*`）。 */
const MOUSE_GESTURES = [
  {
    actionKey: 'Settings_Shortcuts_Mouse_PanViewport_Title',
    gestureKey: 'Settings_Shortcuts_Mouse_PanViewport_Gesture',
  },
  {
    actionKey: 'Settings_Shortcuts_Mouse_ZoomViewport_Title',
    gestureKey: 'Settings_Shortcuts_Mouse_ZoomViewport_Gesture',
  },
  {
    actionKey: 'Settings_Shortcuts_Mouse_ZoomSelection_Title',
    gestureKey: 'Settings_Shortcuts_Mouse_ZoomSelection_Gesture',
  },
  {
    actionKey: 'Settings_Shortcuts_Mouse_RotateSelection_Title',
    gestureKey: 'Settings_Shortcuts_Mouse_RotateSelection_Gesture',
  },
] as const satisfies ReadonlyArray<{
  readonly actionKey: MessageKey
  readonly gestureKey: MessageKey
}>

/** 单个快捷键编辑器属性。 */
interface ShortcutEditorProps {
  readonly title: string
  /** 卡片的搜索锚点（`SETTINGS_ANCHORS` 里的值）。 */
  readonly anchor: string
  readonly binding: string
  readonly otherTitle: string
  readonly otherBinding: string
  readonly onCommit: (next: string) => void
}

/**
 * 快捷键编辑器：输入框 + 清除按钮。
 *
 * 输入框内直接按组合键可录入（只接管带 Ctrl/Alt 的单字符键，避免抢走普通文本编辑）；
 * 也支持手动输入 `Ctrl+Z` 这类文本。失焦或回车时校验并提交。
 */
function ShortcutEditor(props: ShortcutEditorProps): ReactNode {
  // null = 未在编辑，输入框跟随设置；非 null = 用户正在编辑的草稿。
  // 用"草稿优先、提交后归零"的派生写法，避免在 effect 里 setState，
  // 也不会在组件里留一份独立的设置副本（设置仍是唯一事实源）。
  const [draft, setDraft] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const { t } = useI18n()
  const value = draft ?? props.binding

  /** 校验并提交；非法/冲突时只提示、不写入。 */
  const commit = (): void => {
    const text = value.trim()
    if (text === '') {
      setError(null)
      setDraft(null)
      props.onCommit('')
      return
    }
    if (!isValidShortcutBinding(text)) {
      setError(t('Settings_Shortcuts_Error_Invalid'))
      return
    }
    const canonical = canonicalShortcutBinding(text)
    if (canonical === null) {
      setError(t('Settings_Shortcuts_Error_Invalid'))
      return
    }
    if (props.otherBinding !== '' && canonicalShortcutBinding(props.otherBinding) === canonical) {
      setError(t('Settings_Shortcuts_Error_Conflict_Fmt', props.otherTitle))
      return
    }
    setError(null)
    setDraft(null)
    props.onCommit(canonical)
  }

  return (
    <SettingsCard
      title={props.title}
      description="支持 Ctrl / Alt / Shift 组合；留空表示禁用"
      anchor={props.anchor}
    >
      <div className="wb-settings-shortcut">
        <Input
          value={value}
          placeholder="例如 Ctrl+Z"
          aria-label={props.title}
          aria-invalid={error !== null}
          onChange={(_, data) => {
            setDraft(data.value)
            setError(null)
          }}
          onBlur={commit}
          onKeyDown={(event) => {
            if (event.key === 'Escape') {
              setDraft(null)
              setError(null)
              return
            }
            if (event.key === 'Enter') {
              commit()
              return
            }
            // 直接按下组合键即录入；只处理 Ctrl/Alt + 单字符，普通打字保持原样。
            if ((event.ctrlKey || event.altKey) && !event.metaKey && event.key.length === 1) {
              event.preventDefault()
              const parts: string[] = []
              if (event.ctrlKey) {
                parts.push('Ctrl')
              }
              if (event.altKey) {
                parts.push('Alt')
              }
              if (event.shiftKey) {
                parts.push('Shift')
              }
              parts.push(event.key.toUpperCase())
              setDraft(parts.join('+'))
              setError(null)
            }
          }}
        />
        <Button
          appearance="subtle"
          onClick={() => {
            setDraft(null)
            setError(null)
            props.onCommit('')
          }}
        >
          清除
        </Button>
      </div>
      {error !== null && <span className="wb-settings-error">{error}</span>}
    </SettingsCard>
  )
}

/** 快捷键页。 */
export function ShortcutsPage(): ReactNode {
  const settings = useSettings()
  const settingsService = useSettingsService()
  const { undo, redo, conflictReminderEnabled } = settings.keyboardShortcuts
  const { t } = useI18n()

  /** 写入单个槽位（`patch` 会归一化：非法回退默认、冲突清空后者）。 */
  const commitShortcut = (id: ShortcutId, next: string): void => {
    settingsService.patch(
      id === 'undo' ? { keyboardShortcuts: { undo: next } } : { keyboardShortcuts: { redo: next } },
    )
  }

  return (
    <div className="wb-settings-page">
      <SettingsSection title={t('Settings_Shortcuts_Keyboard_SectionTitle')}>
        <ShortcutEditor
          title={t('Settings_Shortcuts_Undo_Title')}
          anchor={SETTINGS_ANCHORS.shortcutsUndo}
          binding={undo}
          otherTitle={t('Settings_Shortcuts_Redo_Title')}
          otherBinding={redo}
          onCommit={(next) => {
            commitShortcut('undo', next)
          }}
        />
        <ShortcutEditor
          title={t('Settings_Shortcuts_Redo_Title')}
          anchor={SETTINGS_ANCHORS.shortcutsRedo}
          binding={redo}
          otherTitle={t('Settings_Shortcuts_Undo_Title')}
          otherBinding={undo}
          onCommit={(next) => {
            commitShortcut('redo', next)
          }}
        />
        <SettingsCard
          title={t('Settings_Shortcuts_ConflictReminder_Header')}
          description="当两个快捷键冲突时给出提醒"
          anchor={SETTINGS_ANCHORS.shortcutsConflictReminder}
        >
          <Switch
            checked={conflictReminderEnabled}
            aria-label={t('Settings_Shortcuts_ConflictReminder_Header')}
            onChange={(_, data) => {
              settingsService.patch({
                keyboardShortcuts: { conflictReminderEnabled: data.checked },
              })
            }}
          />
        </SettingsCard>
        <p className="wb-settings-hint">
          撤销与重做不能同时禁用；两者都留空时会自动保留默认的撤销快捷键。
        </p>
      </SettingsSection>

      <SettingsSection title={t('Settings_Shortcuts_Mouse_SectionTitle')}>
        <p className="wb-settings-hint">{t('Settings_Shortcuts_Mouse_SectionDescription')}</p>
        <ul className="wb-settings-gesture-list">
          {MOUSE_GESTURES.map((item) => (
            <li key={item.actionKey}>
              <span>{t(item.actionKey)}</span>
              <code>{t(item.gestureKey)}</code>
            </li>
          ))}
        </ul>
      </SettingsSection>
    </div>
  )
}
