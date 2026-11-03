/**
 * Copies text to the clipboard. The Clipboard API only exists in secure contexts (https, localhost); on plain http, or when the browser
 * refuses, the old way (a hidden text area and the copy command) is tried. Returns false when nothing worked, so the caller can say so.
 */
export async function copyText(value: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(value)
    return true
  } catch {
    const area = document.createElement('textarea')
    area.value = value
    area.setAttribute('readonly', '')
    area.style.position = 'fixed'
    area.style.opacity = '0'
    document.body.appendChild(area)
    area.select()
    try {
      return document.execCommand('copy')
    } catch {
      return false
    } finally {
      document.body.removeChild(area)
    }
  }
}
