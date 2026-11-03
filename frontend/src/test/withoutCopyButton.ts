/** The text of an element without the "Copy" button of its error message (tests that check the message is shown completely). */
export function textWithoutCopyButton(element: Element): string {
  const clone = element.cloneNode(true) as Element
  clone.querySelectorAll('[data-copy-error]').forEach((node) => node.remove())
  return clone.textContent ?? ''
}
