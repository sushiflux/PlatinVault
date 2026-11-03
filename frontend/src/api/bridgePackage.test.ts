import { afterEach, describe, expect, it, vi } from 'vitest'
import { downloadBridgePackage } from './client'

// The download is a file response saved through a temporary link: the test watches the request and the link's file name
function stubDownload() {
  const fetchMock = vi.fn(async () => new Response(new Blob(['x']), { status: 200 }))
  vi.stubGlobal('fetch', fetchMock)
  URL.createObjectURL = vi.fn(() => 'blob:test')
  URL.revokeObjectURL = vi.fn()
  const names: string[] = []
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
    names.push(this.download)
  })
  return { fetchMock, names }
}

describe('downloadBridgePackage', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    vi.restoreAllMocks()
  })

  it('asks for the Windows ZIP by default, without changing what older servers understand', async () => {
    const { fetchMock, names } = stubDownload()
    await downloadBridgePackage()

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(url).toBe('/api/bridge/package')
    expect(JSON.parse(String(init.body))).toEqual({ origin: window.location.origin, os: 'windows' })
    expect(names).toEqual(['PlatinVault-Bridge.zip'])
  })

  it('asks for the Linux tar.gz and saves it under its own name', async () => {
    const { fetchMock, names } = stubDownload()
    await downloadBridgePackage('linux')

    const [, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(JSON.parse(String(init.body)).os).toBe('linux')
    expect(names).toEqual(['PlatinVault-Bridge-linux-x64.tar.gz'])
  })
})
