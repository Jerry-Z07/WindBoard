import { describe, expect, it } from 'vitest'
import {
  JsonTextWriter,
  escapeJsonString,
  formatUtcDateTimeOffset,
  getJsonProperty,
  isPlainObject,
  parseDateTimeOffset,
  parseJsonTolerant,
} from '../../src/persistence/wbix/json-options.ts'

describe('escapeJsonString：JavaScriptEncoder.Default 逐字符对齐', () => {
  it('HTML 敏感字符与双引号都写成 \\uXXXX', () => {
    // Lead 探针实测：a<b>c&d'e"f+g → a\u003Cb\u003Ec\u0026d\u0027e\u0022f\u002Bg
    expect(escapeJsonString('a<b>c&d\'e"f+g')).toBe(
      '"a\\u003Cb\\u003Ec\\u0026d\\u0027e\\u0022f\\u002Bg"',
    )
  })

  it('非 ASCII 一律转义为大写 \\uXXXX', () => {
    expect(escapeJsonString('中文笔迹')).toBe('"\\u4E2D\\u6587\\u7B14\\u8FF9"')
  })

  it('增补平面字符按代理对拆成两个 \\uXXXX', () => {
    expect(escapeJsonString('😀')).toBe('"\\uD83D\\uDE00"')
  })

  it('反斜杠、制表符、换行用短转义', () => {
    expect(escapeJsonString('C:\\path\\to')).toBe('"C:\\\\path\\\\to"')
    expect(escapeJsonString('\t')).toBe('"\\t"')
    expect(escapeJsonString('\n')).toBe('"\\n"')
    expect(escapeJsonString('\r')).toBe('"\\r"')
    expect(escapeJsonString('\b')).toBe('"\\b"')
    expect(escapeJsonString('\f')).toBe('"\\f"')
  })

  it('其余控制字符写成 \\uXXXX', () => {
    expect(escapeJsonString('\u0001')).toBe('"\\u0001"')
  })

  it('普通 ASCII 原样输出', () => {
    expect(escapeJsonString('page-000.json')).toBe('"page-000.json"')
  })
})

describe('JsonTextWriter：WriteIndented 形态', () => {
  it('2 空格缩进、冒号后一个空格、空数组同行、无尾随换行', () => {
    const writer = new JsonTextWriter()
    writer.openObject()
    writer.stringProperty('format', 'wbix')
    writer.intProperty('version', 3)
    writer.name('pages')
    writer.openArray()
    writer.openObject()
    writer.stringProperty('id', 'x')
    writer.closeObject()
    writer.closeArray()
    writer.name('resources')
    writer.openArray()
    writer.closeArray()
    writer.closeObject()

    const expected = [
      '{',
      '  "format": "wbix",',
      '  "version": 3,',
      '  "pages": [',
      '    {',
      '      "id": "x"',
      '    }',
      '  ],',
      '  "resources": []',
      '}',
    ].join('\r\n')

    expect(writer.text()).toBe(expected)
  })

  it('null 成员照常输出', () => {
    const writer = new JsonTextWriter()
    writer.openObject()
    writer.nullProperty('viewportZoom')
    writer.closeObject()

    expect(writer.text()).toBe('{\r\n  "viewportZoom": null\r\n}')
  })

  it('float 值走 formatF32 规则', () => {
    const writer = new JsonTextWriter()
    writer.openObject()
    writer.f32Property('zoom', 1)
    writer.f32Property('tiny', 1e-7)
    writer.closeObject()

    expect(writer.text()).toBe('{\r\n  "zoom": 1,\r\n  "tiny": 1E-07\r\n}')
  })

  it('时间值不做字符串转义（偏移里的 + 保持字面量）', () => {
    const writer = new JsonTextWriter()
    writer.openObject()
    writer.dateTimeOffsetProperty('createdUtc', new Date('2026-10-31T13:45:12.123Z'))
    writer.closeObject()

    expect(writer.text()).toBe('{\r\n  "createdUtc": "2026-10-31T13:45:12.123+00:00"\r\n}')
  })

  it('空对象同行写成 {}', () => {
    const writer = new JsonTextWriter()
    writer.openObject()
    writer.closeObject()
    expect(writer.text()).toBe('{}')
  })

  it('未闭合容器调用 text() 抛错', () => {
    const writer = new JsonTextWriter()
    writer.openObject()
    expect(() => writer.text()).toThrowError(/容器未闭合/)
  })
})

describe('formatUtcDateTimeOffset：对齐 STJ TrimDateTimeOffset', () => {
  it('毫秒非零保留三位、UTC 写作 +00:00', () => {
    expect(formatUtcDateTimeOffset(new Date('2026-10-31T13:45:12.123Z'))).toBe(
      '2026-10-31T13:45:12.123+00:00',
    )
  })

  it('毫秒为 0 时连小数点一起去掉', () => {
    expect(formatUtcDateTimeOffset(new Date('2026-10-31T13:45:12.000Z'))).toBe(
      '2026-10-31T13:45:12+00:00',
    )
  })

  it('小数秒从尾部去零', () => {
    expect(formatUtcDateTimeOffset(new Date('2026-10-31T13:45:12.120Z'))).toBe(
      '2026-10-31T13:45:12.12+00:00',
    )
  })

  it('无效时间抛错', () => {
    expect(() => formatUtcDateTimeOffset(new Date(Number.NaN))).toThrowError(/时间/)
  })
})

describe('parseDateTimeOffset', () => {
  it('解析 7 位小数秒（桌面形态）', () => {
    const parsed = parseDateTimeOffset('2026-10-31T13:45:12.1234567+00:00')
    expect(parsed).not.toBeNull()
    expect(formatUtcDateTimeOffset(parsed as Date)).toBe('2026-10-31T13:45:12.123+00:00')
  })

  it('解析带偏移的时间并换算到 UTC', () => {
    const parsed = parseDateTimeOffset('2026-10-31T21:45:12+08:00')
    expect(parsed?.toISOString()).toBe('2026-10-31T13:45:12.000Z')
  })

  it('解析 Z 结尾写法', () => {
    const parsed = parseDateTimeOffset('2026-10-31T13:45:12Z')
    expect(parsed?.toISOString()).toBe('2026-10-31T13:45:12.000Z')
  })

  it('非法文本返回 null', () => {
    expect(parseDateTimeOffset('not-a-date')).toBeNull()
    expect(parseDateTimeOffset(123)).toBeNull()
    expect(parseDateTimeOffset(null)).toBeNull()
  })
})

describe('parseJsonTolerant：注释与尾随逗号', () => {
  it('跳过行注释与块注释', () => {
    const text = '{\n  // 行注释\n  "a": 1, /* 块注释 */\n  "b": 2\n}'
    expect(parseJsonTolerant(text)).toEqual({ a: 1, b: 2 })
  })

  it('允许对象与数组的尾随逗号', () => {
    expect(parseJsonTolerant('{"a": [1, 2,],}')).toEqual({ a: [1, 2] })
  })

  it('字符串内的 // 与 , 不受影响', () => {
    expect(parseJsonTolerant('{"url": "http://example.com/a,b"}')).toEqual({
      url: 'http://example.com/a,b',
    })
  })

  it('非法 JSON 抛 json-parse-failed', () => {
    expect(() => parseJsonTolerant('{')).toThrowError(/JSON 解析失败/)
  })
})

describe('getJsonProperty：大小写不敏感', () => {
  it('匹配任意大小写', () => {
    expect(getJsonProperty({ Format: 'wbix' }, 'format')).toEqual({ found: true, value: 'wbix' })
  })

  it('缺失时 found=false', () => {
    expect(getJsonProperty({}, 'format')).toEqual({ found: false, value: undefined })
  })

  it('同名不同大小写时取最后一个', () => {
    expect(getJsonProperty({ format: 'a', FORMAT: 'b' }, 'format').value).toBe('b')
  })
})

describe('isPlainObject', () => {
  it('区分对象、数组与 null', () => {
    expect(isPlainObject({})).toBe(true)
    expect(isPlainObject([])).toBe(false)
    expect(isPlainObject(null)).toBe(false)
    expect(isPlainObject('x')).toBe(false)
  })
})
