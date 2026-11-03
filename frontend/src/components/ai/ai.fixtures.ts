import type { AiProviderId, AiProviderState, AiProviderStatusDto, AiStatusDto } from '../../api/types'

// Shared by the tests of the AI selection: a status answer as GET /api/ai/status would give it.
export function providerStatus(id: AiProviderId, overrides: Partial<AiProviderStatusDto> = {}): AiProviderStatusDto {
  return {
    id,
    hasKey: true,
    model: id === 'gemini' ? 'gemini-3.8-flash' : id === 'claude' ? 'claude-sonnet-4-5' : 'gpt-4o',
    webSearch: id !== 'openai',
    state: 'ok' as AiProviderState,
    since: null,
    until: null,
    message: null,
    ...overrides,
  }
}

export function statusFixture(overrides: Partial<AiStatusDto> = {}): AiStatusDto {
  return {
    mode: 'default',
    defaultProvider: 'gemini',
    autoFallback: true,
    activeProvider: 'gemini',
    providers: [providerStatus('gemini'), providerStatus('claude'), providerStatus('openai', { hasKey: false })],
    ...overrides,
  }
}
