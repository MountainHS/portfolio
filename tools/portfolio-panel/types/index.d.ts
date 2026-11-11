// 패널에서 고른 src/ 폴더 이름 ('' = 전체 프로젝트)
export type ProjectFolder = string

declare module 'claude-code' {
  interface PluginState {
    'portfolio-panel': { project: ProjectFolder }
  }
}
