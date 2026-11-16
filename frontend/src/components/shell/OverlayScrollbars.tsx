import { useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'

// How close (px) to the right edge of a scrolling area the pointer has to be for its thumb to appear
const EDGE = 24
// How long the thumb stays after the last scroll / pointer move near the edge
const LINGER_MS = 900
// Smallest thumb, so a very long page still has something to grab
const MIN_THUMB = 32
// Space between the thumb and the ends / the right edge of its area
const INSET = 2

function isScrollable(element: Element): boolean {
  if (element === document.scrollingElement || element === document.documentElement || element === document.body) {
    const root = document.scrollingElement ?? document.documentElement
    return root.scrollHeight > root.clientHeight + 1
  }
  const style = window.getComputedStyle(element)
  if (!/(auto|scroll|overlay)/.test(style.overflowY)) return false
  return element.scrollHeight > element.clientHeight + 1
}

function rootScroller(): HTMLElement {
  return (document.scrollingElement as HTMLElement | null) ?? document.documentElement
}

// The nearest area that scrolls vertically, starting at the given node (the page itself last)
function scrollableFrom(node: EventTarget | null): HTMLElement | null {
  let element: Element | null = node instanceof Element ? node : null
  while (element && element !== document.body && element !== document.documentElement) {
    if (element instanceof HTMLElement && isScrollable(element)) return element
    element = element.parentElement
  }
  return isScrollable(rootScroller()) ? rootScroller() : null
}

// The box of a scrolling area on screen (the page = the window)
function boxOf(element: HTMLElement): { top: number; right: number; height: number } {
  if (element === rootScroller()) return { top: 0, right: document.documentElement.clientWidth, height: window.innerHeight }
  const rect = element.getBoundingClientRect()
  return { top: rect.top + element.clientTop, right: rect.left + element.clientLeft + element.clientWidth, height: element.clientHeight }
}

/**
 * Overlay scrollbars for the whole app: native bars are hidden (index.css), and this one slim thumb shows for the area that scrolls -
 * while it scrolls, or when the pointer comes near its right edge - and fades out again. It takes no layout space, can be dragged, and
 * keeps the contrast of a control (>= 3:1, token --pv-scroll-thumb). Wheel, touch and keyboard scrolling are the browser's own.
 */
export function OverlayScrollbars() {
  const thumbRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const thumb = thumbRef.current
    if (!thumb) return
    let target: HTMLElement | null = null
    let hideTimer = 0
    let frame = 0
    let near = false
    let drag: { startY: number; startScroll: number; ratio: number } | null = null

    const place = () => {
      frame = 0
      if (!target || !target.isConnected || !isScrollable(target)) {
        thumb.dataset.on = 'false'
        return
      }
      const box = boxOf(target)
      const track = box.height - INSET * 2
      const range = target.scrollHeight - target.clientHeight
      const size = Math.max(MIN_THUMB, Math.round((track * target.clientHeight) / target.scrollHeight))
      const offset = range > 0 ? ((track - size) * target.scrollTop) / range : 0
      thumb.style.top = `${box.top + INSET + offset}px`
      thumb.style.left = `${box.right - INSET}px`
      thumb.style.height = `${size}px`
    }
    const schedule = () => {
      if (!frame) frame = window.requestAnimationFrame(place)
    }
    const show = (element: HTMLElement) => {
      target = element
      thumb.dataset.on = 'true'
      schedule()
      window.clearTimeout(hideTimer)
      hideTimer = window.setTimeout(() => {
        if (!drag && !near && !thumb.matches(':hover')) thumb.dataset.on = 'false'
      }, LINGER_MS)
    }

    const onScroll = (event: Event) => {
      const source = event.target instanceof Document ? rootScroller() : event.target
      if (source instanceof HTMLElement && isScrollable(source)) show(source)
    }
    const onPointerMove = (event: PointerEvent) => {
      if (drag || event.pointerType === 'touch' || event.target === thumb) return
      const element = scrollableFrom(event.target)
      const wasNear = near
      const distance = element ? boxOf(element).right - event.clientX : Infinity
      near = distance <= EDGE && distance >= -2
      if (near && element) show(element)
      else if (wasNear) show(target ?? rootScroller())
    }
    const onResize = () => schedule()

    const onThumbDown = (event: PointerEvent) => {
      if (!target || event.button !== 0) return
      event.preventDefault()
      const box = boxOf(target)
      const track = box.height - INSET * 2
      const size = thumb.getBoundingClientRect().height
      const range = target.scrollHeight - target.clientHeight
      drag = { startY: event.clientY, startScroll: target.scrollTop, ratio: track - size > 0 ? range / (track - size) : 0 }
      thumb.dataset.drag = 'true'
      thumb.setPointerCapture(event.pointerId)
    }
    const onThumbMove = (event: PointerEvent) => {
      if (!drag || !target) return
      target.scrollTop = drag.startScroll + (event.clientY - drag.startY) * drag.ratio
      schedule()
    }
    const onThumbUp = (event: PointerEvent) => {
      if (!drag) return
      drag = null
      thumb.dataset.drag = 'false'
      if (thumb.hasPointerCapture(event.pointerId)) thumb.releasePointerCapture(event.pointerId)
      if (target) show(target)
    }

    document.addEventListener('scroll', onScroll, { capture: true, passive: true })
    document.addEventListener('pointermove', onPointerMove, { passive: true })
    window.addEventListener('resize', onResize)
    thumb.addEventListener('pointerdown', onThumbDown)
    thumb.addEventListener('pointermove', onThumbMove)
    thumb.addEventListener('pointerup', onThumbUp)
    thumb.addEventListener('pointercancel', onThumbUp)
    return () => {
      window.clearTimeout(hideTimer)
      if (frame) window.cancelAnimationFrame(frame)
      document.removeEventListener('scroll', onScroll, { capture: true })
      document.removeEventListener('pointermove', onPointerMove)
      window.removeEventListener('resize', onResize)
      thumb.removeEventListener('pointerdown', onThumbDown)
      thumb.removeEventListener('pointermove', onThumbMove)
      thumb.removeEventListener('pointerup', onThumbUp)
      thumb.removeEventListener('pointercancel', onThumbUp)
    }
  }, [])

  // Decorative for assistive technology: the area itself scrolls with the keyboard as before
  return createPortal(<div ref={thumbRef} className="pv-osb no-print" data-on="false" aria-hidden="true" />, document.body)
}
