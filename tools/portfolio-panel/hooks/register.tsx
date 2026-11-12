import { atom, read, update } from 'claude-code'
import type { EngineInterface, Register } from 'claude-code'

/**
 * 포트폴리오 명령 패널.
 * 채팅 옆 패널에 README 「도구 한눈에」의 명령을 단계별 버튼으로 보여 줍니다.
 * 버튼을 누르면 스킬을 바로 실행(run)하거나, 입력창에 채우기만(fill) 하거나, 말 부탁을 보냅니다(say).
 */

// ── 패널 ──
const PANE_ID = 'portfolio-panel'
const PANE_TITLE = '포트폴리오 명령'
const ALL_PROJECTS = '' // 빈 글 = 전체 프로젝트(폴더 이름을 붙이지 않음)

// ── 버튼 표 (버튼을 더하거나 바꿀 때는 여기만 고칩니다) ──
// kind: run  = 스킬을 바로 실행 (text = 슬래시 없는 명령 이름)
//       fill = 입력창에 "/명령 "만 채우고 Enter는 사용자가 (✋ 명령·주소나 요청 글이 필요한 것)
//       say  = 말 부탁 문장을 바로 보냄 (text = 보낼 문장)
// withProject: true면 위에서 고른 프로젝트 폴더 이름을 명령 뒤에 붙임
type ButtonKind = 'run' | 'fill' | 'say'
type PanelButton = {
  stage: string
  label: string
  kind: ButtonKind
  text: string
  withProject: boolean
}

const STAGES = ['재료', '쓰기', '만들기', '다듬기', '점검', '유지']

const BUTTONS: PanelButton[] = [
  { stage: '재료', label: 'GitHub 레포에서 재료 모으기 ✋', kind: 'fill', text: 'portfolio-intake', withProject: false },
  { stage: '재료', label: 'PPTX 발표 자료 가져오기 ✋', kind: 'fill', text: 'portfolio-intake', withProject: false },
  { stage: '쓰기', label: '가이드 기준 점검', kind: 'run', text: 'portfolio-check', withProject: true },
  { stage: '만들기', label: '데이터 다시 만들기', kind: 'run', text: 'portfolio-build', withProject: true },
  { stage: '만들기', label: '미리보기 띄우기', kind: 'run', text: 'local-preview', withProject: true },
  { stage: '다듬기', label: '발표 덱 장면 바꾸기', kind: 'fill', text: 'portfolio-edit', withProject: true },
  { stage: '다듬기', label: '다른 사이트 느낌(테마)', kind: 'fill', text: 'portfolio-theme', withProject: false },
  { stage: '다듬기', label: '다른 사이트 배치 따라 하기', kind: 'fill', text: 'portfolio-layout', withProject: false },
  { stage: '다듬기', label: '배치까지 새 디자인 ✋', kind: 'fill', text: 'portfolio-custom', withProject: false },
  { stage: '점검', label: '미리보기 배치 점검 결과', kind: 'say', text: '미리보기 배치 점검 결과 알려 줘', withProject: false },
  { stage: '점검', label: '문구 표 점검', kind: 'say', text: '문구 표 점검해 줘', withProject: false },
  { stage: '유지', label: '템플릿 새 기능 받기 ✋', kind: 'fill', text: 'portfolio-update', withProject: false },
  { stage: '유지', label: '충돌 해결 도와줘', kind: 'say', text: '충돌 해결 도와줘', withProject: false },
]

const FOOTER_NOTE =
  '강조색은 profile.md, 절 이름·버튼 문구는 theme/strings.tsv를 직접 고칩니다. PDF·PPTX 저장은 각 페이지의 버튼으로 합니다.'

// ── 상태 ──
const selectedProject = atom({ plugin: 'portfolio-panel', key: 'project' } as const, ALL_PROJECTS)

export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    const isPortfolioFolder = await isPortfolioRepository($)

    if (isPortfolioFolder == false) {
      return next(e)
    }

    await $.command.register({ name: PANE_ID, description: '포트폴리오 명령 패널을 다시 엽니다' })
    void $.ui.open({ id: PANE_ID, title: PANE_TITLE })

    return next(e)
  })

  on('command.run', { command: PANE_ID }, async $ => {
    await $.ui.open({ id: PANE_ID, title: PANE_TITLE })

    return { text: '포트폴리오 명령 패널을 열었습니다.' }
  })

  on('ui.render', { component: 'Pane', requestId: PANE_ID }, async ($, e) => {
    const { Box, Text, Button } = $.ui.resolve(e)
    const projectNames = await listProjectNames($)
    const project = await read($, selectedProject)
    const choices = [ALL_PROJECTS, ...projectNames]

    return (
      <Box flexDirection="column" gap="md">
        <Box flexDirection="column" gap="xs">
          <Text bold>프로젝트</Text>
          <Box flexDirection="row" flexWrap="wrap" gap="xs">
            {choices.map(name => (
              <Button
                key={`project:${name}`}
                label={name === ALL_PROJECTS ? '전체' : name}
                variant={name === project ? 'primary' : 'secondary'}
                onPress={() => update($, selectedProject, () => name)}
              />
            ))}
          </Box>
        </Box>
        {STAGES.map(stage => (
          <Box key={`stage:${stage}`} flexDirection="column" gap="xs">
            <Text bold>{stage}</Text>
            <Box flexDirection="row" flexWrap="wrap" gap="xs">
              {BUTTONS.filter(button => button.stage === stage).map(button => (
                <Button
                  key={`button:${button.label}`}
                  label={button.label}
                  onPress={() => pressButton($, button)}
                />
              ))}
            </Box>
          </Box>
        ))}
        <Text dimColor>✋ 버튼은 입력창에 채우기만 합니다. 이어 쓰고 Enter를 누르세요.</Text>
        <Text dimColor>{FOOTER_NOTE}</Text>
      </Box>
    )
  })
}

/**
 * 버튼 하나를 눌렀을 때의 동작입니다. 패널의 onPress가 부르고, 종류에 따라 엔진에 넘깁니다.
 * run은 스킬을 바로 실행, fill은 입력창에 명령만 채움, say는 문장을 사용자 말로 보냄.
 * @param $ 엔진 인터페이스
 * @param button 누른 버튼 (BUTTONS의 한 줄)
 */
async function pressButton($: EngineInterface, button: PanelButton): Promise<void> {
  const project = await read($, selectedProject)
  const projectArgument = button.withProject ? project : ALL_PROJECTS

  switch (button.kind) {
    case 'run':
      await $.command.run({ command: button.text, args: projectArgument })
      return
    case 'fill':
      await fillPromptBox($, `/${button.text} ${appendSpace(projectArgument)}`)
      return
    case 'say':
      await $.prompt.submit({ text: button.text, asUser: true })
      return
  }
}

/**
 * 입력창에 명령 초안을 넣습니다. 대화 상자가 떠 있어 못 넣었으면 알림으로 알려 줍니다.
 * @param $ 엔진 인터페이스
 * @param text 입력창에 넣을 글 (예: "/portfolio-edit 4-wgo ")
 */
async function fillPromptBox($: EngineInterface, text: string): Promise<void> {
  const filled = await $.prompt.fill({ text })

  if (filled.isFilled == false) {
    $.ui.toast('입력창에 넣지 못했습니다. 열린 대화 상자를 닫고 다시 눌러 주세요.')
  }
}

/**
 * 폴더 이름 뒤에 띄어쓰기를 붙여, 사용자가 바로 이어 쓸 수 있게 합니다.
 * @param projectArgument 붙일 폴더 이름 ('' = 붙이지 않음)
 * @returns "폴더 " 또는 ''
 */
function appendSpace(projectArgument: string): string {
  if (projectArgument === ALL_PROJECTS) {
    return ''
  }

  return `${projectArgument} `
}

/**
 * 작업 폴더가 이 포트폴리오 템플릿인지 봅니다. 다른 레포 세션에서는 패널을 열지 않기 위해 session.start가 부릅니다.
 * @param $ 엔진 인터페이스
 * @returns profile.md와 format/01-portfolio.md가 둘 다 있으면 true
 */
async function isPortfolioRepository($: EngineInterface): Promise<boolean> {
  const hasProfile = await $.fs.exists('profile.md')
  const hasGuide = await $.fs.exists('format/01-portfolio.md')

  return hasProfile && hasGuide
}

/**
 * 프로젝트 버튼 줄에 보일 src/ 폴더 이름들을 읽습니다. `_`로 시작하는 폴더(초안·보관용)는 뺍니다.
 * @param $ 엔진 인터페이스
 * @returns 폴더 이름 목록, 이름순 (src/가 없으면 빈 목록)
 */
async function listProjectNames($: EngineInterface): Promise<string[]> {
  const hasSource = await $.fs.exists('src')

  if (hasSource == false) {
    return []
  }

  const entries = await $.fs.list('src')
  const folders = entries.filter(entry => entry.kind === 'dir' && entry.name.startsWith('_') == false)

  return folders.map(entry => entry.name).sort()
}
