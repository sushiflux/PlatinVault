import { useEffect, useRef, useState, type DragEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { IconAlertCircle, IconPhoto, IconRefresh, IconTrash, IconUpload } from '@tabler/icons-react'
import { useFormat } from '../../i18n/format'
import { CopyErrorButton } from '../ui/CopyErrorButton'
import { ReservedText } from '../ui/ReservedText'
import { COVER_ACCEPT, MAX_COVER_BYTES, coverProblem, type CoverProblem } from './addGameRules'

interface CoverUploadProps {
  // The chosen image (not uploaded yet); null = none
  file: File | null
  onChange: (file: File | null) => void
  // The cover the game has right now (the change-cover dialog): shown until a new file is chosen
  currentUrl?: string | null
  disabled?: boolean
  // Tells the parent that the chosen file breaks a rule (the dialog's "Add" button then waits); a file that breaks a rule is never passed to onChange
  onProblem?: (problem: CoverProblem | null) => void
}

// An object URL for a file, released again; where the browser has no object URLs (tests) there is no preview
function usePreviewUrl(file: File | null): string | null {
  const [url, setUrl] = useState<string | null>(null)
  useEffect(() => {
    if (!file || typeof URL.createObjectURL !== 'function') {
      setUrl(null)
      return
    }
    const created = URL.createObjectURL(file)
    setUrl(created)
    return () => URL.revokeObjectURL(created)
  }, [file])
  return url
}

// The pixel size of the chosen image, read from the browser's own decoding; null until known (and where images are not decoded)
function useImageSize(url: string | null): { width: number; height: number } | null {
  const [size, setSize] = useState<{ width: number; height: number } | null>(null)
  useEffect(() => {
    setSize(null)
    if (!url) return
    let cancelled = false
    const image = new Image()
    image.onload = () => {
      if (!cancelled && image.naturalWidth > 0) setSize({ width: image.naturalWidth, height: image.naturalHeight })
    }
    image.src = url
    return () => {
      cancelled = true
      image.onload = null
    }
  }, [url])
  return size
}

/**
 * The cover field: drop an image onto it or choose a file (PNG, JPEG, WebP, GIF, at most 5 MB, the rules of the server). A file that breaks a rule is
 * refused right away with the reason, in a line whose room is reserved and which has the "Copy" button of every error. The same field serves the
 * add dialog (the file is uploaded after the game is created) and the change-cover dialog of a game's head.
 */
export function CoverUpload({ file, onChange, currentUrl = null, disabled = false, onProblem }: CoverUploadProps) {
  const { t } = useTranslation('dashboard')
  const fmt = useFormat()
  const inputRef = useRef<HTMLInputElement>(null)
  const [dragging, setDragging] = useState(false)
  const [problem, setProblem] = useState<CoverProblem | null>(null)
  const previewUrl = usePreviewUrl(file)
  const dimensions = useImageSize(previewUrl)
  const shownUrl = previewUrl ?? currentUrl

  const maxText = fmt.bytes(MAX_COVER_BYTES)
  const problemText = (p: CoverProblem): string =>
    p.kind === 'type'
      ? t('addGame.cover.type', { type: p.type || t('addGame.cover.typeUnknown') })
      : t('addGame.cover.tooLarge', { size: fmt.bytes(p.bytes), max: fmt.bytes(p.max) })
  // Every text the message line can show: its room is reserved for the longest one
  const candidates = [problemText({ kind: 'type', type: 'application/octet-stream' }), problemText({ kind: 'size', bytes: MAX_COVER_BYTES * 20, max: MAX_COVER_BYTES })]

  const accept = (candidate: File | undefined) => {
    if (!candidate || disabled) return
    const found = coverProblem(candidate)
    setProblem(found)
    onProblem?.(found)
    if (!found) onChange(candidate)
  }
  const clear = () => {
    setProblem(null)
    onProblem?.(null)
    onChange(null)
    if (inputRef.current) inputRef.current.value = ''
  }
  const onDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault()
    setDragging(false)
    accept(event.dataTransfer.files[0])
  }

  const message = problem ? problemText(problem) : ''
  const sizeLine = file ? (dimensions ? t('addGame.cover.dimensions', { width: dimensions.width, height: dimensions.height, size: fmt.bytes(file.size) }) : t('addGame.cover.sizeOnly', { size: fmt.bytes(file.size) })) : ''

  return (
    <div className="pv-cover-up">
      <div
        className="pv-cover-zone"
        data-dragging={dragging ? 'true' : undefined}
        data-invalid={problem ? 'true' : undefined}
        onDragEnter={(e) => {
          e.preventDefault()
          if (!disabled) setDragging(true)
        }}
        onDragOver={(e) => {
          e.preventDefault()
          if (!disabled) setDragging(true)
        }}
        onDragLeave={(e) => {
          if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setDragging(false)
        }}
        onDrop={onDrop}
      >
        <div className="pv-cover-thumb" aria-hidden={shownUrl ? undefined : true}>
          {shownUrl ? <img src={shownUrl} alt={file ? t('addGame.cover.previewAlt') : t('addGame.cover.current')} /> : <IconPhoto size={26} aria-hidden="true" />}
        </div>
        <div className="pv-cover-body">
          {file ? (
            <>
              <p className="pv-cover-name">{file.name}</p>
              <p className="pv-cover-meta">{sizeLine}</p>
              <div className="pv-cover-actions">
                <button type="button" className="pv-btn-text" disabled={disabled} onClick={() => inputRef.current?.click()}>
                  <IconRefresh size={16} aria-hidden="true" />
                  <span>{t('addGame.cover.replace')}</span>
                </button>
                <button type="button" className="pv-btn-text pv-btn-text--danger" disabled={disabled} onClick={clear}>
                  <IconTrash size={16} aria-hidden="true" />
                  <span>{t('addGame.cover.remove')}</span>
                </button>
              </div>
            </>
          ) : (
            <>
              <p className="pv-cover-drop">{t('addGame.cover.drop')}</p>
              <p className="pv-cover-meta">{t('addGame.cover.rules', { max: maxText })}</p>
              <div className="pv-cover-actions">
                <button type="button" className="pv-btn" disabled={disabled} onClick={() => inputRef.current?.click()}>
                  <IconUpload size={18} aria-hidden="true" />
                  <span>{t('addGame.cover.choose')}</span>
                </button>
              </div>
            </>
          )}
        </div>
        <input
          ref={inputRef}
          type="file"
          accept={COVER_ACCEPT}
          className="hidden"
          tabIndex={-1}
          aria-label={t('addGame.cover.dropLabel')}
          disabled={disabled}
          onChange={(e) => {
            accept(e.target.files?.[0])
            // The same file may be chosen again after it was refused
            e.target.value = ''
          }}
        />
      </div>

      {/* The reason a file was refused: its room is reserved, the copy button keeps its place */}
      <div className="pv-field-msg-row">
        <ReservedText className="pv-field-msg" candidates={candidates} role="alert">
          {problem && (
            <span className="pv-field-msg-in" data-tone="error">
              <IconAlertCircle size={16} aria-hidden="true" />
              <span>{message}</span>
            </span>
          )}
        </ReservedText>
        <CopyErrorButton inactive={!problem} report={{ title: t('addGame.cover.problemTitle'), message }} />
      </div>
    </div>
  )
}
