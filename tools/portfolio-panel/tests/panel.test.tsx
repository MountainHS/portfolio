import { expect, test } from 'claude-code/testing'

const PANE = {
  plugin: 'portfolio-panel',
  component: 'Pane',
  requestId: 'portfolio-panel',
  surface: 'desktop',
  props: {
    title: '포트폴리오 명령',
    isFocused: false,
    bodyColumns: 40,
    placement: 'dock',
    scroll: { offset: 0, bodyRows: 40 },
    view: {},
  },
} as const

test('버튼 세 종류가 각각 run·fill·say로 넘어간다', async ($, on) => {
  const runs: { command: string; args?: string }[] = []
  const fills: string[] = []
  const says: string[] = []
  on('fs.exists', () => ({ value: true }))
  on('fs.list', () => ({
    value: [
      { name: '4-wgo', kind: 'dir', size: 0, mtimeMs: 0, isLink: false },
      { name: '_draft', kind: 'dir', size: 0, mtimeMs: 0, isLink: false },
      { name: 'readme.md', kind: 'file', size: 1, mtimeMs: 0, isLink: false },
    ],
  }))
  on('command.run', (_, e) => {
    runs.push({ command: e.command, args: e.args })
    return {}
  })
  on('prompt.fill', (_, e) => {
    fills.push(e.text)
    return { isFilled: true }
  })
  on('prompt.submit', (_, e) => {
    says.push(e.text)
    return { drop: 'test' }
  })

  const ui = await $.ui.mount(PANE)

  expect(await ui.find({ key: 'project:_draft' })).toBeUndefined()
  await ui.press({ key: 'project:4-wgo' })
  await ui.press({ key: 'button:가이드 기준 점검' })
  await ui.press({ key: 'button:발표 덱 장면 바꾸기' })
  await ui.press({ key: 'button:GitHub 레포에서 재료 모으기 ✋' })
  await ui.press({ key: 'button:미리보기 띄우기' })
  await ui.press({ key: 'button:충돌 해결 도와줘' })

  expect(runs).toEqual([
    { command: 'portfolio-check', args: '4-wgo' },
    { command: 'local-preview', args: '4-wgo' },
  ])
  expect(fills).toEqual(['/portfolio-edit 4-wgo ', '/portfolio-intake '])
  expect(says).toEqual(['충돌 해결 도와줘'])
  await ui.unmount()
})
