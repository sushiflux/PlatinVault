import { describe, expect, it } from 'vitest'
import { licenceParagraphs } from './plainLicence'

// The start of the official GNU AGPL v3 text (CRLF as in a Windows checkout)
const START =
  '                    GNU AFFERO GENERAL PUBLIC LICENSE\r\n                       Version 3, 19 November 2007\r\n\r\n' +
  ' Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/>\r\n Everyone is permitted to copy and distribute verbatim copies\r\n' +
  ' of this license document, but changing it is not allowed.\r\n\r\n                            Preamble\r\n\r\n' +
  '  The GNU Affero General Public License is a free, copyleft license for\r\nsoftware and other kinds of works.\r\n'

describe('licenceParagraphs', () => {
  it('keeps the centred title lines apart and joins the hard line breaks of a paragraph', () => {
    expect(licenceParagraphs(START)).toEqual([
      { text: 'GNU AFFERO GENERAL PUBLIC LICENSE', title: true },
      { text: 'Version 3, 19 November 2007', title: true },
      {
        text: 'Copyright (C) 2007 Free Software Foundation, Inc. <https://fsf.org/> Everyone is permitted to copy and distribute verbatim copies of this license document, but changing it is not allowed.',
        title: false,
      },
      { text: 'Preamble', title: true },
      { text: 'The GNU Affero General Public License is a free, copyleft license for software and other kinds of works.', title: false },
    ])
  })

  it('returns nothing for an empty text', () => {
    expect(licenceParagraphs('\n\n')).toEqual([])
  })
})
