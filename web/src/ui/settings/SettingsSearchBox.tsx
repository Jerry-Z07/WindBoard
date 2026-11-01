/**
 * 设置搜索框（桌面 `WindBoard/Settings/SettingsWindow.xaml:23-44` 的 `AutoSuggestBox`）。
 *
 * 桌面行为对照：
 * - 占位符 `SettingsWindow_Search_Placeholder`（`Strings/zh-CN/SettingsWindow.resw`）= “搜索设置”；
 * - `QueryIcon="Find"` → Fluent `SearchBox` 自带的放大镜图标；
 * - 输入即出建议（`TextChanged` → `RefreshSearchSuggestions`），空白输入不出建议；
 * - 选中建议后输入框文本变成该条的显示文本（`OnSettingsSearchSuggestionChosen`），
 *   并跳转到对应设置页（本组件通过 `onSelect` 交给窗口处理）；
 * - 回车提交（`OnSettingsSearchQuerySubmitted`）：有高亮项选高亮项，否则选第一条。
 *
 * 匹配规则全部在 `settings/search-targets.ts` 的纯函数里，本组件只管交互与渲染。
 */

import { SearchBox } from '@fluentui/react-components'
import { useId, useState, type KeyboardEvent, type ReactNode } from 'react'
import {
  searchSettings,
  type SettingsSearchResult,
  type SettingsSearchTarget,
} from '../../settings/search-targets.ts'

/** 属性。 */
export interface SettingsSearchBoxProps {
  /** 选中某条建议后回调（窗口负责切页、滚动与高亮）。 */
  readonly onSelect: (target: SettingsSearchTarget) => void
}

/** 设置搜索框。 */
export function SettingsSearchBox(props: SettingsSearchBoxProps): ReactNode {
  const baseId = useId()
  const listboxId = `${baseId}-listbox`

  const [query, setQuery] = useState('')
  const [open, setOpen] = useState(false)
  const [activeIndex, setActiveIndex] = useState(-1)

  const results = searchSettings(query)
  // 桌面：`string.IsNullOrWhiteSpace(query)` 时不出建议
  const showSuggestions = open && query.trim() !== ''
  const activeDescendant =
    showSuggestions && activeIndex >= 0 && activeIndex < results.length
      ? `${listboxId}-${activeIndex}`
      : undefined

  const closeSuggestions = (): void => {
    setOpen(false)
    setActiveIndex(-1)
  }

  const choose = (result: SettingsSearchResult): void => {
    // 同桌面 `OnSettingsSearchSuggestionChosen`：选中后输入框显示该条的显示文本
    setQuery(result.displayText)
    closeSuggestions()
    props.onSelect(result.target)
  }

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>): void => {
    if (event.key === 'Escape') {
      // Esc 只收起建议列表，不清空已输入文本
      if (showSuggestions) {
        event.preventDefault()
        closeSuggestions()
      }
      return
    }

    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      if (results.length === 0) {
        return
      }
      event.preventDefault()
      setOpen(true)
      setActiveIndex((current) => {
        if (event.key === 'ArrowDown') {
          return current + 1 >= results.length ? 0 : current + 1
        }
        return current <= 0 ? results.length - 1 : current - 1
      })
      return
    }

    if (event.key === 'Enter') {
      if (!showSuggestions || results.length === 0) {
        return
      }
      event.preventDefault()
      // 有高亮项用高亮项，否则用第一条（同桌面 QuerySubmitted 的 `_filteredSearchTargets[0]` 兜底）
      const index = activeIndex >= 0 && activeIndex < results.length ? activeIndex : 0
      choose(results[index])
    }
  }

  return (
    <div className="wb-settings-search">
      <SearchBox
        className="wb-settings-search__input"
        placeholder="搜索设置"
        value={query}
        role="combobox"
        aria-label="搜索设置"
        aria-autocomplete="list"
        aria-expanded={showSuggestions}
        aria-controls={showSuggestions ? listboxId : undefined}
        aria-activedescendant={activeDescendant}
        onChange={(_, data) => {
          setQuery(data.value)
          setOpen(true)
          setActiveIndex(-1)
        }}
        onBlur={closeSuggestions}
        onKeyDown={handleKeyDown}
      />
      {showSuggestions && (
        <div
          className="wb-settings-search__suggestions"
          id={listboxId}
          role="listbox"
          aria-label="搜索建议"
        >
          {results.length === 0 ? (
            <div className="wb-settings-search__empty">没有匹配的设置项</div>
          ) : (
            results.map((result, index) => (
              <div
                key={result.target.anchor}
                id={`${listboxId}-${index}`}
                role="option"
                aria-selected={index === activeIndex}
                className={
                  index === activeIndex
                    ? 'wb-settings-search__suggestion wb-settings-search__suggestion--active'
                    : 'wb-settings-search__suggestion'
                }
                // 用 mousedown 拦下默认行为：否则输入框先失焦收起列表，随后的 click 就落空了
                onMouseDown={(event) => {
                  event.preventDefault()
                }}
                onClick={() => {
                  choose(result)
                }}
              >
                <span className="wb-settings-search__suggestion-title">{result.displayText}</span>
                {result.target.description === undefined ? null : (
                  <span className="wb-settings-search__suggestion-description">
                    {result.target.description}
                  </span>
                )}
              </div>
            ))
          )}
        </div>
      )}
    </div>
  )
}
