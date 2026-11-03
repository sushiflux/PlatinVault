// The licence of PlatinVault is the official plain text of the GNU AGPL v3 (hard line breaks at about 70 characters, centred title
// lines). For reading inside the app the breaks within a paragraph are joined, so the text wraps to the viewer's width; blank lines
// separate paragraphs, a line indented by eight or more spaces (the centred titles) stays on its own and is marked as a title.
// Same rules as SetupWindow.LicenceForDisplay in the Windows installer.

export interface LicenceParagraph {
  text: string
  title: boolean
}

const CENTRED = /^ {8,}\S/

export function licenceParagraphs(source: string): LicenceParagraph[] {
  const paragraphs: LicenceParagraph[] = []
  let current: string[] = []
  const flush = (title = false) => {
    if (current.length > 0) paragraphs.push({ text: current.join(' '), title })
    current = []
  }
  for (const raw of source.replace(/\r\n?/g, '\n').replace(/^﻿/, '').split('\n')) {
    const line = raw.trimEnd()
    if (line.length === 0) {
      flush()
      continue
    }
    if (CENTRED.test(line)) {
      flush()
      current.push(line.trim())
      flush(true)
      continue
    }
    current.push(line.trim())
  }
  flush()
  return paragraphs
}
