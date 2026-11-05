// An ATX heading ("## Title", "## Title ##") of one Markdown line, shared by the Markdown viewer (SimpleMarkdown) and the release notes
// parser. Written without a backtracking pattern on purpose: the former /^ {0,3}(#{1,6})\s+(.*?)\s*#*\s*$/ took cubic time on a long
// heading line with blanks inside (a single 8 KB line froze the page for about a minute), and release notes are text from outside.

const OPENING = /^ {0,3}(#{1,6})\s/

/** The level and the text of a heading line, or null when the line is no heading. Trailing "#" characters and blanks are dropped. */
export function parseAtxHeading(line: string): { level: number; text: string } | null {
  const opening = OPENING.exec(line)
  if (!opening) return null
  let text = line.slice(opening[0].length).trim()
  let end = text.length
  while (end > 0 && text[end - 1] === '#') end--
  text = text.slice(0, end).trimEnd()
  return { level: (opening[1] ?? '#').length, text }
}
